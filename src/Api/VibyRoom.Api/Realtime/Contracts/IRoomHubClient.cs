namespace VibyRoom.Api.Realtime.Contracts;

public interface IRoomHubClient
{
    Task PresenceChanged(
        RoomPresenceSnapshot snapshot);

    Task RoomSubscriptionRevoked(
        RoomSubscriptionRevoked payload);

    Task RoomMemberJoined(
        RoomMemberJoined payload);

    Task RoomMemberLeft(
        RoomMemberLeft payload);

    Task RoomMemberRemoved(
        RoomMemberRemoved payload);

    Task RoomOwnerChanged(
        RoomOwnerChanged payload);

    Task RoomClosed(
        RoomClosed payload);
}