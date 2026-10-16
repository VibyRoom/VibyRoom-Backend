using System.Net;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using VibyRoom.Api.IntegrationTests.Realtime.Infrastructure;
using VibyRoom.Api.Realtime;
using VibyRoom.Api.Realtime.Contracts;
using Xunit;

namespace VibyRoom.Api.IntegrationTests.Realtime;

public sealed class RoomHubIntegrationTests
{
    [Fact]
    public async Task Negotiate_WithoutCookie_ReturnsUnauthorized()
    {
        using var factory = new RoomRealtimeWebApplicationFactory();
        using var client = factory.CreateHttpsClient();

        using var response = await client.PostAsync(
            "/hubs/rooms/negotiate?negotiateVersion=1",
            null,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task Negotiate_CorruptedCookie_ReturnsUnauthorized()
    {
        using var factory = new RoomRealtimeWebApplicationFactory();
        using var client = factory.CreateHttpsClient();

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/hubs/rooms/negotiate?negotiateVersion=1");

        request.Headers.TryAddWithoutValidation(
            "Cookie",
            "VibyRoom.Guest=invalid-ticket");

        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task Subscribe_AuthenticatedMember_ReceivesSnapshotAndEvent()
    {
        using var factory = new RoomRealtimeWebApplicationFactory();

        var guestId = Guid.NewGuid();
        var room = factory.Rooms.SeedRoom(guestId);

        await using var connection = factory.CreateHubConnection(
            factory.CreateGuestCookie(guestId));

        var events = TrackPresence(connection);

        await connection.StartAsync(
            TestContext.Current.CancellationToken);

        var snapshot = await SubscribeAsync(
            connection,
            room.Id);

        Assert.Equal(room.Id, snapshot.RoomId);
        Assert.Equal(
            new[] { guestId },
            snapshot.OnlineGuestIds);

        var received = await ReadPresenceAsync(
            events.Reader,
            value => value.RoomId == room.Id);

        Assert.Equal(
            snapshot.OnlineGuestIds,
            received.OnlineGuestIds);

        Assert.Equal(
            new[] { guestId },
            factory.Services
                .GetRequiredService<RoomPresenceRegistry>()
                .GetOnlineGuests(room.Id));
    }

    [Fact]
    public async Task Subscribe_NonMember_IsDenied()
    {
        using var factory = new RoomRealtimeWebApplicationFactory();

        var ownerId = Guid.NewGuid();
        var outsiderId = Guid.NewGuid();
        var room = factory.Rooms.SeedRoom(ownerId);

        await using var connection = factory.CreateHubConnection(
            factory.CreateGuestCookie(outsiderId));

        await connection.StartAsync(
            TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<HubException>(
            () => SubscribeAsync(connection, room.Id));

        Assert.Contains(
            "roomAccessDenied",
            exception.Message);

        Assert.Empty(
            factory.Services
                .GetRequiredService<RoomPresenceRegistry>()
                .GetOnlineGuests(room.Id));
    }

    [Fact]
    public async Task Subscribe_NonexistentRoom_IsDenied()
    {
        using var factory = new RoomRealtimeWebApplicationFactory();

        var guestId = Guid.NewGuid();

        await using var connection = factory.CreateHubConnection(
            factory.CreateGuestCookie(guestId));

        await connection.StartAsync(
            TestContext.Current.CancellationToken);

        var roomId = Guid.NewGuid();

        await Assert.ThrowsAsync<HubException>(
            () => SubscribeAsync(connection, roomId));

        Assert.Empty(
            factory.Services
                .GetRequiredService<RoomPresenceRegistry>()
                .GetOnlineGuests(roomId));
    }

    [Fact]
    public async Task Subscribe_ClosedRoom_IsDenied()
    {
        using var factory = new RoomRealtimeWebApplicationFactory();

        var ownerId = Guid.NewGuid();
        var room = factory.Rooms.SeedRoom(ownerId);

        room.Close(ownerId);

        await using var connection = factory.CreateHubConnection(
            factory.CreateGuestCookie(ownerId));

        await connection.StartAsync(
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<HubException>(
            () => SubscribeAsync(connection, room.Id));

        Assert.Empty(
            factory.Services
                .GetRequiredService<RoomPresenceRegistry>()
                .GetOnlineGuests(room.Id));
    }

    [Fact]
    public async Task Subscribe_TwoTabs_CountsGuestOnce()
    {
        using var factory = new RoomRealtimeWebApplicationFactory();

        var guestId = Guid.NewGuid();
        var room = factory.Rooms.SeedRoom(guestId);
        var cookie = factory.CreateGuestCookie(guestId);

        await using var first = factory.CreateHubConnection(cookie);
        await using var second = factory.CreateHubConnection(cookie);

        await first.StartAsync(
            TestContext.Current.CancellationToken);

        await second.StartAsync(
            TestContext.Current.CancellationToken);

        var firstSnapshot = await SubscribeAsync(
            first, room.Id);

        var secondSnapshot = await SubscribeAsync(
            second, room.Id);

        Assert.Equal(
            firstSnapshot.OnlineGuestIds,
            secondSnapshot.OnlineGuestIds);

        Assert.Equal(
            new[] { guestId },
            secondSnapshot.OnlineGuestIds);

        var registry = factory.Services
            .GetRequiredService<RoomPresenceRegistry>();

        Assert.Single(registry.GetOnlineGuests(room.Id));
        Assert.Equal(2, registry.GetConnectionIds(room.Id).Count);

        Assert.NotEqual(
            first.ConnectionId,
            second.ConnectionId);
    }

    [Fact]
    public async Task Subscribe_SameConnection_CanJoinTwoRooms()
    {
        using var factory = new RoomRealtimeWebApplicationFactory();

        var guestId = Guid.NewGuid();

        var firstRoom = factory.Rooms.SeedRoom(guestId);
        var secondRoom = factory.Rooms.SeedRoom(guestId);

        await using var connection = factory.CreateHubConnection(
            factory.CreateGuestCookie(guestId));

        await connection.StartAsync(
            TestContext.Current.CancellationToken);

        var firstSnapshot = await SubscribeAsync(
            connection, firstRoom.Id);

        var secondSnapshot = await SubscribeAsync(
            connection, secondRoom.Id);

        Assert.Equal(
            new[] { guestId },
            firstSnapshot.OnlineGuestIds);

        Assert.Equal(
            new[] { guestId },
            secondSnapshot.OnlineGuestIds);

        var registry = factory.Services
            .GetRequiredService<RoomPresenceRegistry>();

        Assert.Equal(
            2,
            registry.GetRoomsForConnection(
                connection.ConnectionId!).Count);
    }

    [Fact]
    public async Task Subscribe_SecondGuest_NotifiesFirstGuest()
    {
        using var factory = new RoomRealtimeWebApplicationFactory();

        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        var room = factory.Rooms.SeedRoom(
            ownerId, memberId);

        await using var owner = factory.CreateHubConnection(
            factory.CreateGuestCookie(ownerId));

        await using var member = factory.CreateHubConnection(
            factory.CreateGuestCookie(memberId));

        var ownerEvents = TrackPresence(owner);

        await owner.StartAsync(
            TestContext.Current.CancellationToken);

        await member.StartAsync(
            TestContext.Current.CancellationToken);

        await SubscribeAsync(owner, room.Id);

        await ReadPresenceAsync(
            ownerEvents.Reader,
            snapshot =>
                snapshot.OnlineGuestIds.Contains(ownerId));

        var memberSnapshot = await SubscribeAsync(
            member, room.Id);

        Assert.Equal(
            2,
            memberSnapshot.OnlineGuestIds.Count);

        var received = await ReadPresenceAsync(
            ownerEvents.Reader,
            snapshot =>
                snapshot.OnlineGuestIds.Count == 2);

        Assert.Contains(ownerId, received.OnlineGuestIds);
        Assert.Contains(memberId, received.OnlineGuestIds);
    }

    [Fact]
    public async Task Unsubscribe_LastTab_NotifiesRemainingMember()
    {
        using var factory = new RoomRealtimeWebApplicationFactory();

        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        var room = factory.Rooms.SeedRoom(
            ownerId, memberId);

        await using var owner = factory.CreateHubConnection(
            factory.CreateGuestCookie(ownerId));

        await using var member = factory.CreateHubConnection(
            factory.CreateGuestCookie(memberId));

        var ownerEvents = TrackPresence(owner);

        await owner.StartAsync(
            TestContext.Current.CancellationToken);

        await member.StartAsync(
            TestContext.Current.CancellationToken);

        await SubscribeAsync(owner, room.Id);

        await ReadPresenceAsync(
            ownerEvents.Reader,
            snapshot =>
                snapshot.OnlineGuestIds.Count == 1);

        await SubscribeAsync(member, room.Id);

        await ReadPresenceAsync(
            ownerEvents.Reader,
            snapshot =>
                snapshot.OnlineGuestIds.Count == 2);

        await member.InvokeAsync(
            "UnsubscribeFromRoom",
            room.Id,
            TestContext.Current.CancellationToken);

        var updated = await ReadPresenceAsync(
            ownerEvents.Reader,
            snapshot =>
                snapshot.OnlineGuestIds.Count == 1);

        Assert.Equal(
            new[] { ownerId },
            updated.OnlineGuestIds);

        Assert.NotNull(
            room.GetRole(memberId));

        var registry = factory.Services
            .GetRequiredService<RoomPresenceRegistry>();

        Assert.Equal(
            new[] { ownerId },
            registry.GetOnlineGuests(room.Id));
    }

    [Fact]
    public async Task Disconnect_AndReconnect_RequireNewSubscription()
    {
        using var factory = new RoomRealtimeWebApplicationFactory();

        var guestId = Guid.NewGuid();
        var room = factory.Rooms.SeedRoom(guestId);
        var cookie = factory.CreateGuestCookie(guestId);

        var registry = factory.Services
            .GetRequiredService<RoomPresenceRegistry>();

        await using (var original =
                     factory.CreateHubConnection(cookie))
        {
            await original.StartAsync(
                TestContext.Current.CancellationToken);

            await SubscribeAsync(original, room.Id);

            Assert.Single(
                registry.GetOnlineGuests(room.Id));

            await original.StopAsync(
                TestContext.Current.CancellationToken);
        }

        await WaitUntilAsync(
            () => registry.GetConnectionIds(room.Id).Count == 0);

        Assert.Empty(
            registry.GetOnlineGuests(room.Id));

        // Persistent membership must survive the disconnect.
        Assert.NotNull(room.GetRole(guestId));

        await using var reconnected =
            factory.CreateHubConnection(cookie);

        await reconnected.StartAsync(
            TestContext.Current.CancellationToken);

        // A new connection has no automatic subscriptions.
        Assert.Empty(registry.GetOnlineGuests(room.Id));

        var snapshot = await SubscribeAsync(
            reconnected, room.Id);

        Assert.Equal(
            new[] { guestId },
            snapshot.OnlineGuestIds);

        Assert.Single(registry.GetConnectionIds(room.Id));
    }

    private static Task<RoomPresenceSnapshot> SubscribeAsync(
        HubConnection connection,
        Guid roomId)
    {
        return connection.InvokeAsync<RoomPresenceSnapshot>(
            "SubscribeToRoom",
            roomId,
            TestContext.Current.CancellationToken);
    }

    private static Channel<RoomPresenceSnapshot> TrackPresence(
        HubConnection connection)
    {
        var channel = Channel
            .CreateUnbounded<RoomPresenceSnapshot>();

        connection.On<RoomPresenceSnapshot>(
            "PresenceChanged",
            snapshot =>
            {
                channel.Writer.TryWrite(snapshot);
            });

        return channel;
    }

    private static async Task<RoomPresenceSnapshot>
        ReadPresenceAsync(
            ChannelReader<RoomPresenceSnapshot> reader,
            Func<RoomPresenceSnapshot, bool> predicate)
    {
        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);

        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        while (true)
        {
            var snapshot = await reader.ReadAsync(
                timeout.Token);

            if (predicate(snapshot))
            {
                return snapshot;
            }
        }
    }

    private static async Task WaitUntilAsync(
        Func<bool> condition)
    {
        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);

        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        while (!condition())
        {
            await Task.Delay(
                TimeSpan.FromMilliseconds(20),
                timeout.Token);
        }
    }
}