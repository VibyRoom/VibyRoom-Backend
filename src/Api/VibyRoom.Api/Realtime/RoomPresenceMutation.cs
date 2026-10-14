using VibyRoom.Api.Realtime.Contracts;

namespace VibyRoom.Api.Realtime;

public sealed record RoomPresenceMutation(
    bool SubscriptionChanged,
    bool OnlineGuestsChanged,
    RoomPresenceSnapshot Snapshot)
{
    public Guid RoomId => Snapshot.RoomId;
}