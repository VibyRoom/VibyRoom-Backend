using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using VibyRoom.Api.Realtime;
using VibyRoom.Application.Abstractions.Rooms;
using Xunit;

namespace VibyRoom.Api.IntegrationTests.Realtime;

public sealed class RoomPresenceCoordinatorTests
{
    [Fact]
    public async Task Subscribe_ActiveMember_RegistersAndPublishes()
    {
        using var fixture = new Fixture();

        var roomId = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        var snapshot = await fixture.Coordinator.SubscribeToRoomAsync(roomId, guestId, "connection-1", TestContext.Current.CancellationToken);

        Assert.Equal(roomId, snapshot.RoomId);
        Assert.Equal(
            new[] { guestId },
            snapshot.OnlineGuestIds);

        Assert.Contains(
            "connection-1",
            fixture.Hub.ConnectionsInGroup(
                RoomPresenceCoordinator.GetGroupName(roomId)));

        Assert.Single(
            fixture.Hub.PresenceFor("connection-1"));
    }

    [Fact]
    public async Task Subscribe_NonMember_DeniesAccess()
    {
        using var fixture = new Fixture();

        fixture.IsMember = (_, _, _) =>
            Task.FromResult(false);

        var roomId = Guid.NewGuid();

        var error = await Assert.ThrowsAsync<HubException>(
            () => fixture.Coordinator.SubscribeToRoomAsync(roomId, Guid.NewGuid(), "connection-1", TestContext.Current.CancellationToken));

        Assert.Equal("roomAccessDenied", error.Message);
        Assert.Empty(fixture.Registry.GetConnectionIds(roomId));
        Assert.Empty(
            fixture.Hub.ConnectionsInGroup(
                RoomPresenceCoordinator.GetGroupName(roomId)));
    }

    [Fact]
    public async Task Subscribe_Repeated_IsIdempotent()
    {
        using var fixture = new Fixture();

        var roomId = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        await fixture.Coordinator.SubscribeToRoomAsync(roomId, guestId, "connection-1", TestContext.Current.CancellationToken);

        await fixture.Coordinator.SubscribeToRoomAsync(roomId, guestId, "connection-1", TestContext.Current.CancellationToken);

        Assert.Single(fixture.Registry.GetConnectionIds(roomId));
        Assert.Single(
            fixture.Hub.PresenceFor("connection-1"));
    }

    [Fact]
    public async Task Subscribe_SecondTab_DoesNotPublishFalseChange()
    {
        using var fixture = new Fixture();

        var roomId = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        await fixture.Coordinator.SubscribeToRoomAsync(roomId, guestId, "tab-1", TestContext.Current.CancellationToken);

        await fixture.Coordinator.SubscribeToRoomAsync(roomId, guestId, "tab-2", TestContext.Current.CancellationToken);

        Assert.Single(fixture.Registry.GetOnlineGuests(roomId));
        Assert.Equal(2, fixture.Registry.GetConnectionIds(roomId).Count);

        Assert.Single(fixture.Hub.PresenceFor("tab-1"));
        Assert.Empty(fixture.Hub.PresenceFor("tab-2"));
    }

    [Fact]
    public async Task Subscribe_GroupAdditionFails_DoesNotRegister()
    {
        using var fixture = new Fixture();

        fixture.Hub.FailAdd = true;

        var roomId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<HubException>(
            () => fixture.Coordinator.SubscribeToRoomAsync(roomId, Guid.NewGuid(), "connection-1", TestContext.Current.CancellationToken));

        Assert.Equal("roomSubscriptionFailed", exception.Message);
        Assert.Empty(fixture.Registry.GetConnectionIds(roomId));
    }

    [Fact]
    public async Task Subscribe_CancelledAfterGroupAdd_Compensates()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();

        var roomId = Guid.NewGuid();
        var groupName =
            RoomPresenceCoordinator.GetGroupName(roomId);

        fixture.Hub.AfterAdd = cancellation.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Coordinator.SubscribeToRoomAsync(
                roomId,
                Guid.NewGuid(),
                "connection-1",
                cancellation.Token));

        Assert.Empty(fixture.Registry.GetConnectionIds(roomId));
        Assert.Empty(
            fixture.Hub.ConnectionsInGroup(groupName));
    }

    [Fact]
    public async Task Unsubscribe_RemovesOnlyCurrentConnection()
    {
        using var fixture = new Fixture();

        var roomId = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        await fixture.Coordinator.SubscribeToRoomAsync(roomId, guestId, "tab-1", TestContext.Current.CancellationToken);

        await fixture.Coordinator.SubscribeToRoomAsync(roomId, guestId, "tab-2", TestContext.Current.CancellationToken);

        await fixture.Coordinator.UnsubscribeFromRoomAsync(roomId, "tab-1", TestContext.Current.CancellationToken);

        Assert.Equal(
            new[] { "tab-2" },
            fixture.Registry.GetConnectionIds(roomId));

        Assert.Equal(
            new[] { guestId },
            fixture.Registry.GetOnlineGuests(roomId));
    }

    [Fact]
    public async Task Unsubscribe_Repeated_DoesNotPublish()
    {
        using var fixture = new Fixture();

        var roomId = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        await fixture.Coordinator.SubscribeToRoomAsync(roomId, guestId, "connection-1", TestContext.Current.CancellationToken);

        await fixture.Coordinator.UnsubscribeFromRoomAsync(roomId, "connection-1", TestContext.Current.CancellationToken);

        var count = fixture.Hub
            .PresenceFor("connection-1").Count;

        await fixture.Coordinator.UnsubscribeFromRoomAsync(roomId, "connection-1", TestContext.Current.CancellationToken);

        Assert.Equal(
            count,
            fixture.Hub.PresenceFor("connection-1").Count);
    }

    [Fact]
    public async Task Disconnect_RemovesSubscriptionsFromAllRooms()
    {
        using var fixture = new Fixture();

        var firstRoom = Guid.NewGuid();
        var secondRoom = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        await fixture.Coordinator.SubscribeToRoomAsync(firstRoom, guestId, "connection-1", TestContext.Current.CancellationToken);

        await fixture.Coordinator.SubscribeToRoomAsync(secondRoom, guestId, "connection-1", TestContext.Current.CancellationToken);

        await fixture.Coordinator.DisconnectAsync("connection-1");

        Assert.Empty(fixture.Registry.GetOnlineGuests(firstRoom));
        Assert.Empty(fixture.Registry.GetOnlineGuests(secondRoom));
        Assert.Empty(
            fixture.Registry.GetRoomsForConnection("connection-1"));
    }

    [Fact]
    public async Task RevokeMember_RemovesAllTabsAndNotifiesRemaining()
    {
        using var fixture = new Fixture();

        var roomId = Guid.NewGuid();
        var removedGuest = Guid.NewGuid();
        var remainingGuest = Guid.NewGuid();

        await fixture.Coordinator.SubscribeToRoomAsync(roomId, removedGuest, "removed-tab-1", TestContext.Current.CancellationToken);

        await fixture.Coordinator.SubscribeToRoomAsync(roomId, removedGuest, "removed-tab-2", TestContext.Current.CancellationToken);

        await fixture.Coordinator.SubscribeToRoomAsync(roomId, remainingGuest, "remaining-tab", TestContext.Current.CancellationToken);

        await fixture.Coordinator.RevokeMemberAsync(roomId, removedGuest, TestContext.Current.CancellationToken);

        Assert.Equal(
            new[] { remainingGuest },
            fixture.Registry.GetOnlineGuests(roomId));

        Assert.Equal(
            new[] { "remaining-tab" },
            fixture.Registry.GetConnectionIds(roomId));

        foreach (var tab in new[]
                 { "removed-tab-1", "removed-tab-2" })
        {
            var revoked = Assert.Single(
                fixture.Hub.RevocationsFor(tab));

            Assert.Equal(roomId, revoked.RoomId);
            Assert.Equal("membershipEnded", revoked.Reason);
        }

        Assert.Equal(
            new[] { remainingGuest },
            fixture.Hub.PresenceFor("remaining-tab")
                .Last().OnlineGuestIds);
    }

    [Fact]
    public async Task RevokeMember_FailedGroupRemoval_DoesNotLeakEvents()
    {
        using var fixture = new Fixture();

        var roomId = Guid.NewGuid();
        var revokedGuest = Guid.NewGuid();
        var remainingGuest = Guid.NewGuid();

        await fixture.Coordinator.SubscribeToRoomAsync(roomId, revokedGuest, "revoked", TestContext.Current.CancellationToken);

        await fixture.Coordinator.SubscribeToRoomAsync(roomId, remainingGuest, "remaining", TestContext.Current.CancellationToken);

        var eventCount = fixture.Hub.PresenceFor("revoked").Count;

        fixture.Hub.FailRemove = true;

        await fixture.Coordinator.RevokeMemberAsync(roomId, revokedGuest, TestContext.Current.CancellationToken);

        // The physical group still contains the revoked
        // connection, simulating a SignalR infrastructure failure.
        Assert.Contains(
            "revoked",
            fixture.Hub.ConnectionsInGroup(
                RoomPresenceCoordinator.GetGroupName(roomId)));

        Assert.DoesNotContain(
            "revoked",
            fixture.Registry.GetConnectionIds(roomId));

        await fixture.Coordinator.SubscribeToRoomAsync(roomId, Guid.NewGuid(), "new-member", TestContext.Current.CancellationToken);

        // Private events must be routed through Registry,
        // never through the stale physical group.
        Assert.Equal(
            eventCount,
            fixture.Hub.PresenceFor("revoked").Count);

        Assert.NotEmpty(
            fixture.Hub.PresenceFor("remaining"));
    }

    [Fact]
    public async Task RevokeRoom_ClearsAllAndSendsRoomClosed()
    {
        using var fixture = new Fixture();

        var roomId = Guid.NewGuid();

        await fixture.Coordinator.SubscribeToRoomAsync(roomId, Guid.NewGuid(), "connection-1", TestContext.Current.CancellationToken);

        await fixture.Coordinator.SubscribeToRoomAsync(roomId, Guid.NewGuid(), "connection-2", TestContext.Current.CancellationToken);

        await fixture.Coordinator.RevokeRoomAsync(roomId, TestContext.Current.CancellationToken);

        Assert.Empty(fixture.Registry.GetConnectionIds(roomId));

        foreach (var connectionId in new[]
                 { "connection-1", "connection-2" })
        {
            var payload = Assert.Single(
                fixture.Hub.RevocationsFor(connectionId));

            Assert.Equal(roomId, payload.RoomId);
            Assert.Equal("roomClosed", payload.Reason);
        }
    }

    [Fact]
    public async Task Subscribe_ConcurrentWithRevoke_FinalStateIsOffline()
    {
        using var fixture = new Fixture();

        var roomId = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        var entered = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var release = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        fixture.IsMember = async (_, _, _) =>
        {
            entered.TrySetResult(true);
            return await release.Task;
        };

        var subscribeTask =
            fixture.Coordinator.SubscribeToRoomAsync(roomId, guestId, "connection-1", TestContext.Current.CancellationToken);

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        // Revocation must wait for the room semaphore
        // held by the in-progress subscription.
        var revokeTask = fixture.Coordinator.RevokeMemberAsync(roomId, guestId, TestContext.Current.CancellationToken);

        release.TrySetResult(true);

        await subscribeTask;
        await revokeTask;

        Assert.Empty(fixture.Registry.GetConnectionIds(roomId));
        Assert.Empty(fixture.Registry.GetOnlineGuests(roomId));

        Assert.Single(
            fixture.Hub.RevocationsFor("connection-1"));
    }

    [Fact]
    public async Task Subscribe_AfterMembershipRevoked_IsDenied()
    {
        using var fixture = new Fixture();

        var roomId = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        await fixture.Coordinator.SubscribeToRoomAsync(roomId, guestId, "old-connection", TestContext.Current.CancellationToken);

        await fixture.Coordinator.RevokeMemberAsync(roomId, guestId, TestContext.Current.CancellationToken);

        fixture.IsMember = (_, _, _) =>
            Task.FromResult(false);

        await Assert.ThrowsAsync<HubException>(
            () => fixture.Coordinator.SubscribeToRoomAsync(roomId, guestId, "new-connection", TestContext.Current.CancellationToken));

        Assert.Empty(fixture.Registry.GetConnectionIds(roomId));
    }

    [Fact]
    public async Task Subscribe_PresenceDeliveryFails_RegistrationSurvives()
    {
        using var fixture = new Fixture();

        fixture.Hub.FailPresenceDelivery = true;

        var roomId = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        var snapshot =
            await fixture.Coordinator.SubscribeToRoomAsync(roomId, guestId, "connection-1", TestContext.Current.CancellationToken);

        Assert.Equal(
            new[] { guestId },
            snapshot.OnlineGuestIds);

        Assert.Equal(
            new[] { "connection-1" },
            fixture.Registry.GetConnectionIds(roomId));
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
            return _query(roomId, guestId, cancellationToken);
        }
    }
}