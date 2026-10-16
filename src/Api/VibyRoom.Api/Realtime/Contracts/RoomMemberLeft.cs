namespace VibyRoom.Api.Realtime.Contracts;

public sealed record RoomMemberLeft(
    Guid RoomId,
    Guid GuestId);