namespace VibyRoom.Api.Realtime.Contracts;

public sealed record RoomMemberJoined(
    Guid RoomId,
    RoomMemberEvent Member);