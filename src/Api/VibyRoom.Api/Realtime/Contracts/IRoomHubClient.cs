namespace VibyRoom.Api.Realtime.Contracts;

public interface IRoomHubClient
{
    Task PresenceChanged(
        RoomPresenceSnapshot snapshot);

    Task RoomSubscriptionRevoked(
        RoomSubscriptionRevoked payload);
}