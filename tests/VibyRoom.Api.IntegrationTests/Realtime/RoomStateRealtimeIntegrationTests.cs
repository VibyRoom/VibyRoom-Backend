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

public sealed class RoomStateRealtimeIntegrationTests
{
    private const string CsrfHeader = "X-CSRF-TOKEN";
    private const string CsrfCookieName = "VibyRoom.Antiforgery=";

    [Fact]
    public async Task JoinViaRest_DeliversMemberJoined()
    {
        using var factory = new RoomRealtimeWebApplicationFactory();
        using var http = factory.CreateHttpsClient();

        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var room = factory.Rooms.SeedRoom(ownerId);

        var ownerCookie = factory.CreateGuestCookie(ownerId);
        var memberCookie = factory.CreateGuestCookie(memberId);

        await using var owner = factory.CreateHubConnection(
            ownerCookie);

        var joinedEvents = Track<RoomMemberJoined>(
            owner, "RoomMemberJoined");

        var ownerChangedEvents = Track<RoomOwnerChanged>(
            owner, "RoomOwnerChanged");

        await owner.StartAsync(TestContext.Current.CancellationToken);
        await SubscribeAsync(owner, room.Id);

        var memberSession = await GetRestSessionAsync(
            http, memberCookie);

        using var response = await SendMutationAsync(
            http,
            HttpMethod.Post,
            $"/api/v1/invitations/{room.InviteCode}/join",
            memberSession,
            new { DisplayName = "Alex" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var received = await ReadEventAsync(joinedEvents);

        Assert.Equal(room.Id, received.RoomId);
        Assert.Equal(memberId, received.Member.GuestId);
        Assert.Equal("Alex", received.Member.DisplayName);
        Assert.Equal("member", received.Member.Role);

        var member = Assert.Single(
            room.Members,
            x => x.GuestId == memberId);

        Assert.Equal(member.JoinedAtUtc, received.Member.JoinedAtUtc);

        // REST Join must not create SignalR presence.
        var registry = factory.Services
            .GetRequiredService<RoomPresenceRegistry>();

        Assert.Equal(
            new[] { ownerId },
            registry.GetOnlineGuests(room.Id));

        // Idempotent Join must not publish again.
        using var repeated = await SendMutationAsync(
            http,
            HttpMethod.Post,
            $"/api/v1/invitations/{room.InviteCode}/join",
            memberSession,
            new { DisplayName = "Changed name" });

        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);

        // A subsequent event on the same connection acts
        // as an ordering barrier for the repeated Join.
        var ownerSession = await GetRestSessionAsync(
            http, ownerCookie);

        using var transfer = await SendMutationAsync(
            http,
            HttpMethod.Put,
            $"/api/v1/rooms/{room.Id}/owner",
            ownerSession,
            new { NewOwnerGuestId = memberId });

        Assert.Equal(HttpStatusCode.NoContent, transfer.StatusCode);

        await ReadEventAsync(ownerChangedEvents);

        Assert.False(joinedEvents.TryRead(out _));
    }

    [Fact]
    public async Task LeaveViaRest_RevokesMemberAndNotifiesOwner()
    {
        using var factory = new RoomRealtimeWebApplicationFactory();
        using var http = factory.CreateHttpsClient();

        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var room = factory.Rooms.SeedRoom(ownerId, memberId);

        var ownerCookie = factory.CreateGuestCookie(ownerId);
        var memberCookie = factory.CreateGuestCookie(memberId);

        await using var owner = factory.CreateHubConnection(ownerCookie);
        await using var member = factory.CreateHubConnection(memberCookie);

        var leftEvents = Track<RoomMemberLeft>(
            owner, "RoomMemberLeft");

        var revokedEvents = Track<RoomSubscriptionRevoked>(
            member, "RoomSubscriptionRevoked");

        await owner.StartAsync(TestContext.Current.CancellationToken);
        await member.StartAsync(TestContext.Current.CancellationToken);

        await SubscribeAsync(owner, room.Id);
        await SubscribeAsync(member, room.Id);

        var memberSession = await GetRestSessionAsync(
            http, memberCookie);

        using var response = await SendMutationAsync(
            http,
            HttpMethod.Delete,
            $"/api/v1/rooms/{room.Id}/members/me",
            memberSession);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var left = await ReadEventAsync(leftEvents);
        var revoked = await ReadEventAsync(revokedEvents);

        Assert.Equal(room.Id, left.RoomId);
        Assert.Equal(memberId, left.GuestId);

        Assert.Equal(room.Id, revoked.RoomId);
        Assert.Equal("membershipEnded", revoked.Reason);

        var registry = factory.Services
            .GetRequiredService<RoomPresenceRegistry>();

        Assert.DoesNotContain(
            member.ConnectionId!,
            registry.GetConnectionIds(room.Id));

        Assert.Null(room.GetRole(memberId));

        await Assert.ThrowsAsync<HubException>(
            () => SubscribeAsync(member, room.Id));
    }

    [Fact]
    public async Task RemoveViaRest_RevokesBothTabs()
    {
        using var factory = new RoomRealtimeWebApplicationFactory();
        using var http = factory.CreateHttpsClient();

        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var room = factory.Rooms.SeedRoom(ownerId, memberId);

        var ownerCookie = factory.CreateGuestCookie(ownerId);
        var memberCookie = factory.CreateGuestCookie(memberId);

        await using var owner = factory.CreateHubConnection(ownerCookie);
        await using var first = factory.CreateHubConnection(memberCookie);
        await using var second = factory.CreateHubConnection(memberCookie);

        var removedEvents = Track<RoomMemberRemoved>(
            owner, "RoomMemberRemoved");

        var firstRevocations = Track<RoomSubscriptionRevoked>(
            first, "RoomSubscriptionRevoked");

        var secondRevocations = Track<RoomSubscriptionRevoked>(
            second, "RoomSubscriptionRevoked");

        var firstRemovedEvents = Track<RoomMemberRemoved>(
            first, "RoomMemberRemoved");

        var secondRemovedEvents = Track<RoomMemberRemoved>(
            second, "RoomMemberRemoved");

        await owner.StartAsync(TestContext.Current.CancellationToken);
        await first.StartAsync(TestContext.Current.CancellationToken);
        await second.StartAsync(TestContext.Current.CancellationToken);

        await SubscribeAsync(owner, room.Id);
        await SubscribeAsync(first, room.Id);
        await SubscribeAsync(second, room.Id);

        var ownerSession = await GetRestSessionAsync(
            http, ownerCookie);

        using var response = await SendMutationAsync(
            http,
            HttpMethod.Delete,
            $"/api/v1/rooms/{room.Id}/members/{memberId}",
            ownerSession);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var removed = await ReadEventAsync(removedEvents);
        var firstRevoked = await ReadEventAsync(firstRevocations);
        var secondRevoked = await ReadEventAsync(secondRevocations);

        Assert.Equal(room.Id, removed.RoomId);
        Assert.Equal(memberId, removed.GuestId);

        Assert.Equal("membershipEnded", firstRevoked.Reason);
        Assert.Equal("membershipEnded", secondRevoked.Reason);

        Assert.False(firstRemovedEvents.TryRead(out _));
        Assert.False(secondRemovedEvents.TryRead(out _));

        var registry = factory.Services
            .GetRequiredService<RoomPresenceRegistry>();

        Assert.Equal(
            new[] { owner.ConnectionId! },
            registry.GetConnectionIds(room.Id));

        await Assert.ThrowsAsync<HubException>(
            () => SubscribeAsync(first, room.Id));

        await Assert.ThrowsAsync<HubException>(
            () => SubscribeAsync(second, room.Id));
    }

    [Fact]
    public async Task TransferOwnershipViaRest_NotifiesBothGuests()
    {
        using var factory = new RoomRealtimeWebApplicationFactory();
        using var http = factory.CreateHttpsClient();

        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var room = factory.Rooms.SeedRoom(ownerId, memberId);

        var ownerCookie = factory.CreateGuestCookie(ownerId);
        var memberCookie = factory.CreateGuestCookie(memberId);

        await using var owner = factory.CreateHubConnection(ownerCookie);
        await using var member = factory.CreateHubConnection(memberCookie);

        var ownerEvents = Track<RoomOwnerChanged>(
            owner, "RoomOwnerChanged");

        var memberEvents = Track<RoomOwnerChanged>(
            member, "RoomOwnerChanged");

        await owner.StartAsync(TestContext.Current.CancellationToken);
        await member.StartAsync(TestContext.Current.CancellationToken);

        await SubscribeAsync(owner, room.Id);
        await SubscribeAsync(member, room.Id);

        var registry = factory.Services
            .GetRequiredService<RoomPresenceRegistry>();

        var onlineBefore = registry
            .GetOnlineGuests(room.Id)
            .Order()
            .ToArray();

        var ownerSession = await GetRestSessionAsync(
            http, ownerCookie);

        using var response = await SendMutationAsync(
            http,
            HttpMethod.Put,
            $"/api/v1/rooms/{room.Id}/owner",
            ownerSession,
            new { NewOwnerGuestId = memberId });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var ownerEvent = await ReadEventAsync(ownerEvents);
        var memberEvent = await ReadEventAsync(memberEvents);

        Assert.Equal(ownerId, ownerEvent.PreviousOwnerGuestId);
        Assert.Equal(memberId, ownerEvent.NewOwnerGuestId);

        Assert.Equal(ownerEvent, memberEvent);

        Assert.Equal(
            onlineBefore,
            registry.GetOnlineGuests(room.Id).Order().ToArray());

        Assert.Equal(memberId, room.OwnerGuestId);

        // Repeated transfer to the current Owner is idempotent.
        var memberSession = await GetRestSessionAsync(
            http, memberCookie);

        using var repeated = await SendMutationAsync(
            http,
            HttpMethod.Put,
            $"/api/v1/rooms/{room.Id}/owner",
            memberSession,
            new { NewOwnerGuestId = memberId });

        Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);

        Assert.False(ownerEvents.TryRead(out _));
        Assert.False(memberEvents.TryRead(out _));
    }

    [Fact]
    public async Task CloseViaRest_DeliversClosedBeforeRevocation()
    {
        using var factory = new RoomRealtimeWebApplicationFactory();
        using var http = factory.CreateHttpsClient();

        var ownerId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var room = factory.Rooms.SeedRoom(ownerId, memberId);

        var ownerCookie = factory.CreateGuestCookie(ownerId);
        var memberCookie = factory.CreateGuestCookie(memberId);

        await using var owner = factory.CreateHubConnection(ownerCookie);
        await using var member = factory.CreateHubConnection(memberCookie);

        var ownerOrder = Channel.CreateUnbounded<string>();
        var memberOrder = Channel.CreateUnbounded<string>();

        owner.On<RoomClosed>(
            "RoomClosed",
            payload =>
            {
                if (payload.RoomId == room.Id)
                {
                    ownerOrder.Writer.TryWrite("closed");
                }
            });

        owner.On<RoomSubscriptionRevoked>(
            "RoomSubscriptionRevoked",
            payload =>
            {
                if (payload.RoomId == room.Id &&
                    payload.Reason == "roomClosed")
                {
                    ownerOrder.Writer.TryWrite("revoked");
                }
            });

        member.On<RoomClosed>(
            "RoomClosed",
            payload =>
            {
                if (payload.RoomId == room.Id)
                {
                    memberOrder.Writer.TryWrite("closed");
                }
            });

        member.On<RoomSubscriptionRevoked>(
            "RoomSubscriptionRevoked",
            payload =>
            {
                if (payload.RoomId == room.Id &&
                    payload.Reason == "roomClosed")
                {
                    memberOrder.Writer.TryWrite("revoked");
                }
            });

        await owner.StartAsync(TestContext.Current.CancellationToken);
        await member.StartAsync(TestContext.Current.CancellationToken);

        await SubscribeAsync(owner, room.Id);
        await SubscribeAsync(member, room.Id);

        var ownerSession = await GetRestSessionAsync(
            http, ownerCookie);

        using var response = await SendMutationAsync(
            http,
            HttpMethod.Post,
            $"/api/v1/rooms/{room.Id}/close",
            ownerSession);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.Equal("closed", await ReadEventAsync(ownerOrder.Reader));
        Assert.Equal("revoked", await ReadEventAsync(ownerOrder.Reader));

        Assert.Equal("closed", await ReadEventAsync(memberOrder.Reader));
        Assert.Equal("revoked", await ReadEventAsync(memberOrder.Reader));

        var registry = factory.Services
            .GetRequiredService<RoomPresenceRegistry>();

        Assert.Empty(registry.GetConnectionIds(room.Id));
        Assert.Empty(registry.GetOnlineGuests(room.Id));

        await Assert.ThrowsAsync<HubException>(
            () => SubscribeAsync(member, room.Id));
    }

    [Fact]
    public async Task SoleOwnerLeave_DeliversOnlyRoomClosed()
    {
        using var factory = new RoomRealtimeWebApplicationFactory();
        using var http = factory.CreateHttpsClient();

        var ownerId = Guid.NewGuid();
        var room = factory.Rooms.SeedRoom(ownerId);
        var ownerCookie = factory.CreateGuestCookie(ownerId);

        await using var owner = factory.CreateHubConnection(ownerCookie);

        var closedEvents = Track<RoomClosed>(
            owner, "RoomClosed");

        var leftEvents = Track<RoomMemberLeft>(
            owner, "RoomMemberLeft");

        var revokedEvents = Track<RoomSubscriptionRevoked>(
            owner, "RoomSubscriptionRevoked");

        await owner.StartAsync(TestContext.Current.CancellationToken);
        await SubscribeAsync(owner, room.Id);

        var session = await GetRestSessionAsync(
            http, ownerCookie);

        using var response = await SendMutationAsync(
            http,
            HttpMethod.Delete,
            $"/api/v1/rooms/{room.Id}/members/me",
            session);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.Equal(
            room.Id,
            (await ReadEventAsync(closedEvents)).RoomId);

        Assert.Equal(
            "roomClosed",
            (await ReadEventAsync(revokedEvents)).Reason);

        Assert.False(leftEvents.TryRead(out _));
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

    private static ChannelReader<T> Track<T>(
        HubConnection connection,
        string methodName)
    {
        var channel = Channel.CreateUnbounded<T>();

        connection.On<T>(
            methodName,
            payload =>
            {
                channel.Writer.TryWrite(payload);
            });

        return channel.Reader;
    }

    private static async Task<T> ReadEventAsync<T>(
        ChannelReader<T> reader)
    {
        using var timeout =
            CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken);

        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        return await reader.ReadAsync(timeout.Token);
    }

    private static async Task<(string Cookie, string Token)>
        GetRestSessionAsync(
            HttpClient client,
            string guestCookie)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/v1/security/antiforgery");

        request.Headers.TryAddWithoutValidation(
            "Cookie",
            guestCookie);

        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var token = await response.Content
            .ReadFromJsonAsync<AntiforgeryTokenResponse>(
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.NotNull(token);
        Assert.Equal(CsrfHeader, token.HeaderName);

        Assert.True(
            response.Headers.TryGetValues(
                "Set-Cookie",
                out var setCookies));

        var antiforgeryCookie = setCookies!
            .Select(value => value.Split(';')[0])
            .FirstOrDefault(value =>
                value.StartsWith(
                    CsrfCookieName,
                    StringComparison.Ordinal));

        Assert.NotNull(antiforgeryCookie);

        return (
            $"{guestCookie}; {antiforgeryCookie}",
            token.RequestToken);
    }

    private static async Task<HttpResponseMessage>
        SendMutationAsync(
            HttpClient client,
            HttpMethod method,
            string url,
            (string Cookie, string Token) session,
            object? body = null)
    {
        using var request = new HttpRequestMessage(
            method,
            url);

        request.Headers.TryAddWithoutValidation(
            "Cookie",
            session.Cookie);

        request.Headers.TryAddWithoutValidation(
            CsrfHeader,
            session.Token);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }
}