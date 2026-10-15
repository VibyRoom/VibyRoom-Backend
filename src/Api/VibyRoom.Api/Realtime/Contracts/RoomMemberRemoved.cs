namespace VibyRoom.Api.Realtime.Contracts;

public sealed record RoomMemberRemoved(
    Guid RoomId,
    Guid GuestId);