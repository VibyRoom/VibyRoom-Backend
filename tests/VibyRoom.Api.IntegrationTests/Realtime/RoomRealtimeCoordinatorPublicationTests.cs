using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VibyRoom.Api.Realtime;
using VibyRoom.Api.Realtime.Contracts;
using VibyRoom.Application.Abstractions.Rooms;
using Xunit;

namespace VibyRoom.Api.IntegrationTests.Realtime;

public sealed class RoomRealtimeCoordinatorPublicationTests
{
    [Fact]
    public async Task MemberJoined_OnlyCurrentSubscribersReceiveEvent()
    {
        using var fixture = new Fixture();

        var roomId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        await fixture.SubscribeAsync(
            roomId, ownerId, "owner");

        var payload = new RoomMemberJoined(
            roomId,
            new RoomMemberEvent(
                memberId,
                "Alex",
                DateTimeOffset.UtcNow));

        await fixture.Coordinator.PublishRoomEventAsync(roomId, client => client.RoomMemberJoined(payload), TestContext.Current.CancellationToken);

        Assert.Equal(
            payload,
            Assert.Single(fixture.Hub.MemberJoinedFor("owner")));

        Assert.Empty(
            fixture.Hub.MemberJoinedFor("not-subscribed"));

        Assert.Equal(
            new[] { ownerId },
            fixture.Registry.GetOnlineGuests(roomId));
    }

    [Fact]
    public async Task MemberRemoved_RevokesAllTabsBeforePublication()
    {
        using var fixture = new Fixture();

        var roomId = Guid.NewGuid();
        var removedId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();

        await fixture.SubscribeAsync(
            roomId, removedId, "removed-1");

        await fixture.SubscribeAsync(
            roomId, removedId, "removed-2");

        await fixture.SubscribeAsync(
            roomId, ownerId, "owner");

        var payload = new RoomMemberRemoved(
            roomId, removedId);

        await fixture.Coordinator.RevokeMemberAndPublishAsync(roomId, removedId, client => client.RoomMemberRemoved(payload), TestContext.Current.CancellationToken);

        Assert.Equal(
            payload,
            Assert.Single(fixture.Hub.MemberRemovedFor("owner")));

        foreach (var connectionId in new[]
                 { "removed-1", "removed-2" })
        {
            Assert.Empty(
                fixture.Hub.MemberRemovedFor(connectionId));

            var revoked = Assert.Single(
                fixture.Hub.RevocationsFor(connectionId));

            Assert.Equal("membershipEnded", revoked.Reason);
        }

        Assert.Equal(
            new[] { "owner" },
            fixture.Registry.GetConnectionIds(roomId));
    }

    [Fact]
    public async Task StaleSignalRGroup_DoesNotLeakPrivateEvents()
    {
        using var fixture = new Fixture();

        var roomId = Guid.NewGuid();
        var removedId = Guid.NewGuid();

        await fixture.SubscribeAsync(
            roomId, removedId, "removed");

        await fixture.SubscribeAsync(
            roomId, Guid.NewGuid(), "remaining");

        fixture.Hub.FailRemove = true;

        await fixture.Coordinator.RevokeMemberAndPublishAsync(roomId, removedId, client => client.RoomMemberRemoved(
                new RoomMemberRemoved(roomId, removedId)), TestContext.Current.CancellationToken);

        Assert.Contains(
            "removed",
            fixture.Hub.ConnectionsInGroup(
                RoomPresenceCoordinator.GetGroupName(roomId)));

        Assert.DoesNotContain(
            "removed",
            fixture.Registry.GetConnectionIds(roomId));

        Assert.Empty(
            fixture.Hub.MemberRemovedFor("removed"));

        Assert.Single(
            fixture.Hub.MemberRemovedFor("remaining"));
    }

    [Fact]
    public async Task OwnerChanged_DoesNotModifyPresence()
    {
        using var fixture = new Fixture();

        var roomId = Guid.NewGuid();
        var oldOwner = Guid.NewGuid();
        var newOwner = Guid.NewGuid();

        await fixture.SubscribeAsync(
            roomId, oldOwner, "old-owner");

        await fixture.SubscribeAsync(
            roomId, newOwner, "new-owner");

        var before = fixture.Registry.GetOnlineGuests(roomId);
        var presenceCount =
            fixture.Hub.PresenceFor("old-owner").Count;

        var payload = new RoomOwnerChanged(
            roomId, oldOwner, newOwner);

        await fixture.Coordinator.PublishRoomEventAsync(roomId, client => client.RoomOwnerChanged(payload), TestContext.Current.CancellationToken);

        Assert.Equal(
            payload,
            Assert.Single(fixture.Hub.OwnerChangedFor("old-owner")));

        Assert.Equal(
            payload,
            Assert.Single(fixture.Hub.OwnerChangedFor("new-owner")));

        Assert.Equal(
            before,
            fixture.Registry.GetOnlineGuests(roomId));

        Assert.Equal(
            presenceCount,
            fixture.Hub.PresenceFor("old-owner").Count);
    }

    [Fact]
    public async Task RoomClosed_PublishesBeforeClearingRegistry()
    {
        using var fixture = new Fixture();

        var roomId = Guid.NewGuid();

        await fixture.SubscribeAsync(
            roomId, Guid.NewGuid(), "client-1");

        await fixture.SubscribeAsync(
            roomId, Guid.NewGuid(), "client-2");

        IReadOnlyList<string> recipientsAtPublish = [];

        await fixture.Coordinator.PublishRoomClosedAndRevokeAsync(roomId, client =>
            {
                recipientsAtPublish =
                    fixture.Registry.GetConnectionIds(roomId);

                return client.RoomClosed(
                    new RoomClosed(roomId));
            }, TestContext.Current.CancellationToken);

        Assert.Equal(2, recipientsAtPublish.Count);

        foreach (var connectionId in new[]
                 { "client-1", "client-2" })
        {
            Assert.Equal(
                roomId,
                Assert.Single(
                    fixture.Hub.RoomClosedFor(connectionId))
                    .RoomId);

            Assert.Equal(
                "roomClosed",
                Assert.Single(
                    fixture.Hub.RevocationsFor(connectionId))
                    .Reason);
        }

        Assert.Empty(fixture.Registry.GetConnectionIds(roomId));
        Assert.Empty(fixture.Registry.GetOnlineGuests(roomId));

        Assert.Empty(
            fixture.Hub.ConnectionsInGroup(
                RoomPresenceCoordinator.GetGroupName(roomId)));
    }

    [Fact]
    public async Task RoomClosed_DeliveryFailureStillRevokes()
    {
        using var fixture = new Fixture();

        var roomId = Guid.NewGuid();

        await fixture.SubscribeAsync(
            roomId, Guid.NewGuid(), "client");

        fixture.Hub.FailRoomEventDelivery = true;

        await fixture.Coordinator.PublishRoomClosedAndRevokeAsync(roomId, client => client.RoomClosed(
                new RoomClosed(roomId)), TestContext.Current.CancellationToken);

        Assert.Empty(
            fixture.Hub.RoomClosedFor("client"));

        Assert.Empty(
            fixture.Registry.GetConnectionIds(roomId));

        Assert.Equal(
            "roomClosed",
            Assert.Single(
                fixture.Hub.RevocationsFor("client"))
                .Reason);
    }

    [Fact]
    public async Task MemberLeft_IgnoresCancelledPostCommitToken()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();

        var roomId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        await fixture.SubscribeAsync(
            roomId, memberId, "member");

        await fixture.SubscribeAsync(
            roomId, Guid.NewGuid(), "remaining");

        cancellation.Cancel();

        await fixture.Coordinator.RevokeMemberAndPublishAsync(
            roomId,
            memberId,
            client => client.RoomMemberLeft(
                new RoomMemberLeft(roomId, memberId)),
            cancellation.Token);

        Assert.Empty(
            fixture.Registry.GetRoomsForConnection("member"));

        Assert.Single(
            fixture.Hub.MemberLeftFor("remaining"));

        Assert.Empty(
            fixture.Hub.MemberLeftFor("member"));
    }

    [Fact]
    public async Task ConcurrentSubscribeAndRevoke_DoesNotRetainMember()
    {
        using var fixture = new Fixture();

        var roomId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        var entered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var release = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        fixture.IsMember = async (_, _, _) =>
        {
            entered.TrySetResult(true);
            return await release.Task;
        };

        var subscription = fixture.Coordinator.SubscribeToRoomAsync(roomId, memberId, "member", TestContext.Current.CancellationToken);

        await entered.Task.WaitAsync(
            TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);

        var revocation =
            fixture.Coordinator.RevokeMemberAndPublishAsync(roomId, memberId, client => client.RoomMemberRemoved(
                    new RoomMemberRemoved(roomId, memberId)), TestContext.Current.CancellationToken);

        release.TrySetResult(true);

        await Task.WhenAll(subscription, revocation);

        Assert.Empty(fixture.Registry.GetConnectionIds(roomId));

        Assert.Single(
            fixture.Hub.RevocationsFor("member"));

        Assert.Empty(
            fixture.Hub.MemberRemovedFor("member"));
    }

    [Fact]
    public async Task ClosedRoom_RejectsNewSubscription()
    {
        using var fixture = new Fixture();

        var roomId = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        await fixture.SubscribeAsync(
            roomId, guestId, "old");

        await fixture.Coordinator.PublishRoomClosedAndRevokeAsync(roomId, client => client.RoomClosed(
                new RoomClosed(roomId)), TestContext.Current.CancellationToken);

        fixture.IsMember = (_, _, _) =>
            Task.FromResult(false);

        var error = await Assert.ThrowsAsync<HubException>(
            () => fixture.Coordinator.SubscribeToRoomAsync(
                roomId,
                guestId,
                "new",
                TestContext.Current.CancellationToken));

        Assert.Equal("roomAccessDenied", error.Message);
        Assert.Empty(fixture.Registry.GetConnectionIds(roomId));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _provider;

        public Fixture()
        {
            _provider = new ServiceCollection()
                .AddScoped<IRoomMembershipQuery>(
                    _ => new MembershipQuery(
                        (roomId, guestId, token) =>
                            IsMember(roomId, guestId, token)))
                .BuildServiceProvider(validateScopes: true);

            Coordinator = new RoomPresenceCoordinator(
                Registry,
                _provider.GetRequiredService<IServiceScopeFactory>(),
                Hub,
                NullLogger<RoomPresenceCoordinator>.Instance);
        }

        public RoomPresenceRegistry Registry { get; } = new();

        public RecordingRoomHubContext Hub { get; } = new();

        public RoomPresenceCoordinator Coordinator { get; }

        public Func<Guid, Guid, CancellationToken, Task<bool>>
            IsMember { get; set; } =
                (_, _, _) => Task.FromResult(true);

        public Task SubscribeAsync(
            Guid roomId,
            Guid guestId,
            string connectionId)
        {
            return Coordinator.SubscribeToRoomAsync(
                roomId,
                guestId,
                connectionId,
                TestContext.Current.CancellationToken);
        }

        public void Dispose()
        {
            Coordinator.Dispose();
            _provider.Dispose();
        }
    }

    private sealed class MembershipQuery : IRoomMembershipQuery
    {
        private readonly Func<
            Guid, Guid, CancellationToken, Task<bool>> _query;

        public MembershipQuery(
            Func<Guid, Guid, CancellationToken, Task<bool>> query)
        {
            _query = query;
        }

        public Task<bool> IsActiveMemberAsync(
            Guid roomId,
            Guid guestId,
            CancellationToken cancellationToken = default)
        {
            return _query(
                roomId,
                guestId,
                cancellationToken);
        }
    }
}