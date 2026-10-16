using VibyRoom.Api.Realtime.Contracts;

namespace VibyRoom.Api.Realtime;

public sealed record RoomPresenceRevocation(
    IReadOnlyList<string> ConnectionIds,
    bool OnlineGuestsChanged,
    RoomPresenceSnapshot Snapshot);