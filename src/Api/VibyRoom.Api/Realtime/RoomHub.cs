using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using VibyRoom.Api.Authentication;
using VibyRoom.Api.Realtime.Contracts;

namespace VibyRoom.Api.Realtime;

[Authorize(
    AuthenticationSchemes =
        GuestAuthenticationDefaults.AuthenticationScheme)]
public sealed class RoomHub : Hub<IRoomHubClient>
{
    private readonly RoomPresenceCoordinator _coordinator;

    public RoomHub(RoomPresenceCoordinator coordinator)
    {
        _coordinator = coordinator
            ?? throw new ArgumentNullException(
                nameof(coordinator));
    }

    public Task<RoomPresenceSnapshot> SubscribeToRoom(
        Guid roomId)
    {
        var guestId = GetAuthenticatedGuestId();

        return _coordinator.SubscribeToRoomAsync(
            roomId,
            guestId,
            Context.ConnectionId,
            Context.ConnectionAborted);
    }

    public Task UnsubscribeFromRoom(Guid roomId)
    {
        // Authorization is required even for unsubscribe.
        _ = GetAuthenticatedGuestId();

        return _coordinator.UnsubscribeFromRoomAsync(
            roomId,
            Context.ConnectionId,
            Context.ConnectionAborted);
    }

    public override async Task OnDisconnectedAsync(
        Exception? exception)
    {
        try
        {
            await _coordinator.DisconnectAsync(
                Context.ConnectionId);
        }
        finally
        {
            await base.OnDisconnectedAsync(exception);
        }
    }

    private Guid GetAuthenticatedGuestId()
    {
        var principal = Context.User;

        if (principal?.Identity is not ClaimsIdentity identity ||
            !identity.IsAuthenticated ||
            identity.AuthenticationType !=
                GuestAuthenticationDefaults.AuthenticationScheme)
        {
            throw new HubException("invalidGuest");
        }

        var guestIdClaims = principal
            .FindAll(GuestAuthenticationDefaults.GuestIdClaimType)
            .ToArray();

        if (guestIdClaims.Length != 1 ||
            !Guid.TryParse(
                guestIdClaims[0].Value,
                out var guestId) ||
            guestId == Guid.Empty)
        {
            throw new HubException("invalidGuest");
        }

        return guestId;
    }
}