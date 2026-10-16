using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using VibyRoom.Api.Contracts;
using VibyRoom.Api.IntegrationTests.Realtime.Infrastructure;
using VibyRoom.Api.Realtime;
using VibyRoom.Api.Realtime.Contracts;
using Xunit;

namespace VibyRoom.Api.IntegrationTests.Realtime;

public sealed class RoomStateRealtimeNegativeIntegrationTests
{
    private const string CsrfHeader = "X-CSRF-TOKEN";

    [Fact]
    public async Task InvalidJoin_DoesNotPublishMemberJoined()
    {
        using var factory = new RoomRealtimeWebApplicationFactory();
        using var http = factory.CreateHttpsClient();

        var ownerId = Guid.NewGuid();
        var joiningId = Guid.NewGuid();
        var room = factory.Rooms.SeedRoom(ownerId);

        await using var owner = factory.CreateHubConnection(
            factory.CreateGuestCookie(ownerId));

        var events = TrackRoomEvents(owner);

        await owner.StartAsync(TestContext.Current.CancellationToken);
        await SubscribeAsync(owner, room.Id);

        var session = await GetSessionAsync(
            http,
            factory.CreateGuestCookie(joiningId));

        using var invalid = await SendAsync(
            http,
            HttpMethod.Post,
            $"/api/v1/invitations/{room.InviteCode}/join",
            session,
            new { DisplayName = " " });

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var valid = await SendAsync(
            http,
            HttpMethod.Post,
            $"/api/v1/invitations/{room.InviteCode}/join",
            session,
            new { DisplayName = "Alex" });

        Assert.Equal(HttpStatusCode.Created, valid.StatusCode);

        var received = await ReadAsync(events);

        Assert.Equal("joined", received.Kind);
        Assert.Equal(room.Id, received.RoomId);
        Assert.Equal(joiningId, received.GuestId);
        Assert.False(events.TryRead(out _));
    }

    [Fact]
    public async Task MissingCsrf_DoesNotPublishMemberJoined()
    {
        using var factory = new RoomRealtimeWebApplicationFactory();
        using var http = factory.CreateHttpsClient();

        var ownerId = Guid.NewGuid();
        var joiningId = Guid.NewGuid();
        var room = factory.Rooms.SeedRoom(ownerId);

        await using var owner = factory.CreateHubConnection(
            factory.CreateGuestCookie(ownerId));

        var events = TrackRoomEvents(owner);

        await owner.StartAsync(TestContext.Current.CancellationToken);
        await SubscribeAsync(owner, room.Id);

        var session = await GetSessionAsync(
            http,
            factory.CreateGuestCookie(joiningId));

        using var rejected = await SendAsync(
            http,
            HttpMethod.Post,
            $"/api/v1/invitations/{room.InviteCode}/join",
            session,
            new { DisplayName = "Alex" },
            includeCsrf: false);

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        using var accepted = await SendAsync(
            http,
            HttpMethod.Post,
            $"/api/v1/invitations/{room.InviteCode}/join",
            session,
            new { DisplayName = "Alex" });

        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);

        var received = await ReadAsync(events);

        Assert.Equal("joined", received.Kind);
        Assert.Equal(joiningId, received.GuestId);
        Assert.False(events.TryRead(out _));
    }

    [Fact]
    public async Task NonOwnerCannotRemoveMember_OrRevokePresence()
    {
        using var factory = new RoomRealtimeWebApplicationFactory();
        using var http = factory.CreateHttpsClient();

        var ownerId = Guid.NewGuid();
        var attackerId = Guid.NewGuid();
        var targetId = Guid.NewGuid();

        var room = factory.Rooms.SeedRoom(
            ownerId, attackerId, targetId);

        await using var owner = factory.CreateHubConnection(
            factory.CreateGuestCookie(ownerId));

        await using var target = factory.CreateHubConnection(
            factory.CreateGuestCookie(targetId));

        var events = TrackRoomEvents(owner);

        await owner.StartAsync(TestContext.Current.CancellationToken);
        await target.StartAsync(TestContext.Current.CancellationToken);

        await SubscribeAsync(owner, room.Id);
        await SubscribeAsync(target, room.Id);

        var attackerSession = await GetSessionAsync(
            http,
            factory.CreateGuestCookie(attackerId));

        using var rejected = await SendAsync(
            http,
            HttpMethod.Delete,
            $"/api/v1/rooms/{room.Id}/members/{targetId}",
            attackerSession);

        Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);

        var registry = factory.Services
            .GetRequiredService<RoomPresenceRegistry>();

        Assert.Contains(
            target.ConnectionId!,
            registry.GetConnectionIds(room.Id));

        var ownerSession = await GetSessionAsync(
            http,
            factory.CreateGuestCookie(ownerId));

        using var accepted = await SendAsync(
            http,
            HttpMethod.Delete,
            $"/api/v1/rooms/{room.Id}/members/{targetId}",
            ownerSession);

        Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);

        var received = await ReadAsync(events);

        Assert.Equal("removed", received.Kind);
        Assert.Equal(room.Id, received.RoomId);
        Assert.Equal(targetId, received.GuestId);

        Assert.False(events.TryRead(out _));

        Assert.DoesNotContain(
            target.ConnectionId!,
            registry.GetConnectionIds(room.Id));
    }

    [Fact]
    public async Task SaveFailure_DoesNotRevokeOrPublish()
    {
        using var factory = new RoomRealtimeWebApplicationFactory();
        using var http = factory.CreateHttpsClient();

        var ownerId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var joiningId = Guid.NewGuid();

        var failingRoom = factory.Rooms.SeedRoom(ownerId, targetId);
        var controlRoom = factory.Rooms.SeedRoom(ownerId);

        var ownerCookie = factory.CreateGuestCookie(ownerId);

        await using var owner = factory.CreateHubConnection(
            ownerCookie);

        await using var target = factory.CreateHubConnection(
            factory.CreateGuestCookie(targetId));

        var events = TrackRoomEvents(owner);

        await owner.StartAsync(TestContext.Current.CancellationToken);
        await target.StartAsync(TestContext.Current.CancellationToken);

        await SubscribeAsync(owner, failingRoom.Id);
        await SubscribeAsync(owner, controlRoom.Id);
        await SubscribeAsync(target, failingRoom.Id);

        var ownerSession = await GetSessionAsync(
            http, ownerCookie);

        factory.Rooms.FailSave = true;

        try
        {
            using var failed = await SendAsync(
                http,
                HttpMethod.Delete,
                $"/api/v1/rooms/{failingRoom.Id}/members/{targetId}",
                ownerSession);

            Assert.Equal(
                HttpStatusCode.InternalServerError,
                failed.StatusCode);
        }
        finally
        {
            factory.Rooms.FailSave = false;
        }

        var registry = factory.Services
            .GetRequiredService<RoomPresenceRegistry>();

        Assert.Contains(
            target.ConnectionId!,
            registry.GetConnectionIds(failingRoom.Id));

        // Control event on a different room.
        // The failing in-memory aggregate is not reused.
        var joiningSession = await GetSessionAsync(
            http,
            factory.CreateGuestCookie(joiningId));

        using var accepted = await SendAsync(
            http,
            HttpMethod.Post,
            $"/api/v1/invitations/{controlRoom.InviteCode}/join",
            joiningSession,
            new { DisplayName = "Control member" });

        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);

        var received = await ReadAsync(events);

        Assert.Equal("joined", received.Kind);
        Assert.Equal(controlRoom.Id, received.RoomId);
        Assert.Equal(joiningId, received.GuestId);

        Assert.False(events.TryRead(out _));
    }

    [Fact]
    public async Task ConcurrencyConflict_DoesNotPublishOwnerChanged()
    {
        using var factory = new RoomRealtimeWebApplicationFactory();
        using var http = factory.CreateHttpsClient();

        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var joiningId = Guid.NewGuid();

        var failingRoom = factory.Rooms.SeedRoom(
            ownerId, memberId);

        var controlRoom = factory.Rooms.SeedRoom(ownerId);

        var ownerCookie = factory.CreateGuestCookie(ownerId);

        await using var owner = factory.CreateHubConnection(ownerCookie);

        var events = TrackRoomEvents(owner);

        await owner.StartAsync(TestContext.Current.CancellationToken);

        await SubscribeAsync(owner, failingRoom.Id);
        await SubscribeAsync(owner, controlRoom.Id);

        var ownerSession = await GetSessionAsync(
            http, ownerCookie);

        factory.Rooms.FailConcurrency = true;

        try
        {
            using var failed = await SendAsync(
                http,
                HttpMethod.Put,
                $"/api/v1/rooms/{failingRoom.Id}/owner",
                ownerSession,
                new { NewOwnerGuestId = memberId });

            Assert.Equal(HttpStatusCode.Conflict, failed.StatusCode);
        }
        finally
        {
            factory.Rooms.FailConcurrency = false;
        }

        var joiningSession = await GetSessionAsync(
            http,
            factory.CreateGuestCookie(joiningId));

        using var accepted = await SendAsync(
            http,
            HttpMethod.Post,
            $"/api/v1/invitations/{controlRoom.InviteCode}/join",
            joiningSession,
            new { DisplayName = "Control member" });

        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);

        var received = await ReadAsync(events);

        Assert.Equal("joined", received.Kind);
        Assert.Equal(controlRoom.Id, received.RoomId);
        Assert.False(events.TryRead(out _));
    }

    [Fact]
    public async Task OutsiderDoesNotReceivePrivateRoomEvents()
    {
        using var factory = new RoomRealtimeWebApplicationFactory();
        using var http = factory.CreateHttpsClient();

        var ownerId = Guid.NewGuid();
        var outsiderId = Guid.NewGuid();
        var firstJoiningId = Guid.NewGuid();
        var secondJoiningId = Guid.NewGuid();

        var privateRoom = factory.Rooms.SeedRoom(ownerId);

        // Outsider is allowed to subscribe to the control room,
        // but has no membership in privateRoom.
        var controlRoom = factory.Rooms.SeedRoom(
            ownerId, outsiderId);

        await using var outsider = factory.CreateHubConnection(
            factory.CreateGuestCookie(outsiderId));

        var events = TrackRoomEvents(outsider);

        await outsider.StartAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<HubException>(
            () => SubscribeAsync(outsider, privateRoom.Id));

        await SubscribeAsync(outsider, controlRoom.Id);

        var firstSession = await GetSessionAsync(
            http,
            factory.CreateGuestCookie(firstJoiningId));

        using var privateJoin = await SendAsync(
            http,
            HttpMethod.Post,
            $"/api/v1/invitations/{privateRoom.InviteCode}/join",
            firstSession,
            new { DisplayName = "Private member" });

        Assert.Equal(HttpStatusCode.Created, privateJoin.StatusCode);

        var secondSession = await GetSessionAsync(
            http,
            factory.CreateGuestCookie(secondJoiningId));

        using var controlJoin = await SendAsync(
            http,
            HttpMethod.Post,
            $"/api/v1/invitations/{controlRoom.InviteCode}/join",
            secondSession,
            new { DisplayName = "Control member" });

        Assert.Equal(HttpStatusCode.Created, controlJoin.StatusCode);

        var received = await ReadAsync(events);

        Assert.Equal("joined", received.Kind);
        Assert.Equal(controlRoom.Id, received.RoomId);
        Assert.Equal(secondJoiningId, received.GuestId);

        Assert.False(events.TryRead(out _));

        var registry = factory.Services
            .GetRequiredService<RoomPresenceRegistry>();

        Assert.DoesNotContain(
            outsider.ConnectionId!,
            registry.GetConnectionIds(privateRoom.Id));

        Assert.Contains(
            outsider.ConnectionId!,
            registry.GetConnectionIds(controlRoom.Id));
    }

    private static ChannelReader<ObservedRoomEvent> TrackRoomEvents(
        HubConnection connection)
    {
        var channel =
            Channel.CreateUnbounded<ObservedRoomEvent>();

        connection.On<RoomMemberJoined>(
            "RoomMemberJoined",
            payload =>
            {
                channel.Writer.TryWrite(
                    new ObservedRoomEvent(
                        "joined",
                        payload.RoomId,
                        payload.Member.GuestId));
            });

        connection.On<RoomMemberRemoved>(
            "RoomMemberRemoved",
            payload =>
            {
                channel.Writer.TryWrite(
                    new ObservedRoomEvent(
                        "removed",
                        payload.RoomId,
                        payload.GuestId));
            });

        connection.On<RoomOwnerChanged>(
            "RoomOwnerChanged",
            payload =>
            {
                channel.Writer.TryWrite(
                    new ObservedRoomEvent(
                        "owner-changed",
                        payload.RoomId,
                        payload.NewOwnerGuestId));
            });

        connection.On<RoomMemberLeft>(
            "RoomMemberLeft",
            payload =>
            {
                channel.Writer.TryWrite(
                    new ObservedRoomEvent(
                        "left",
                        payload.RoomId,
                        payload.GuestId));
            });

        connection.On<RoomClosed>(
            "RoomClosed",
            payload =>
            {
                channel.Writer.TryWrite(
                    new ObservedRoomEvent(
                        "closed",
                        payload.RoomId,
                        Guid.Empty));
            });

        return channel.Reader;
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

    private static async Task<ObservedRoomEvent> ReadAsync(
        ChannelReader<ObservedRoomEvent> reader)
    {
        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);

        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        return await reader.ReadAsync(timeout.Token);
    }

    private static async Task<(string Cookie, string Token)>
        GetSessionAsync(
            HttpClient http,
            string guestCookie)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/security/antiforgery");

        request.Headers.TryAddWithoutValidation(
            "Cookie",
            guestCookie);

        using var response = await http.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var tokens = await response.Content
            .ReadFromJsonAsync<AntiforgeryTokenResponse>(
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.NotNull(tokens);
        Assert.Equal(CsrfHeader, tokens.HeaderName);

        Assert.True(
            response.Headers.TryGetValues(
                "Set-Cookie",
                out var cookies));

        var csrfCookie = cookies!
            .Select(cookie => cookie.Split(';')[0])
            .FirstOrDefault(cookie =>
                cookie.StartsWith(
                    "VibyRoom.Antiforgery=",
                    StringComparison.Ordinal));

        Assert.NotNull(csrfCookie);

        return (
            $"{guestCookie}; {csrfCookie}",
            tokens.RequestToken);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient http,
        HttpMethod method,
        string path,
        (string Cookie, string Token) session,
        object? body = null,
        bool includeCsrf = true)
    {
        using var request = new HttpRequestMessage(
            method,
            path);

        request.Headers.TryAddWithoutValidation(
            "Cookie",
            session.Cookie);

        if (includeCsrf)
        {
            request.Headers.TryAddWithoutValidation(
                CsrfHeader,
                session.Token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await http.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private readonly record struct ObservedRoomEvent(
        string Kind,
        Guid RoomId,
        Guid GuestId);
}