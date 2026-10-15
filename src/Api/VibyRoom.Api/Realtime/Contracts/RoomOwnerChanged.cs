namespace VibyRoom.Api.Realtime.Contracts;

public sealed record RoomOwnerChanged(
    Guid RoomId,
    Guid PreviousOwnerGuestId,
    Guid NewOwnerGuestId);