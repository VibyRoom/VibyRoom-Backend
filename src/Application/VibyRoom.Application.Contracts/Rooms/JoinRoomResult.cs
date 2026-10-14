namespace VibyRoom.Application.Contracts.Rooms;

public sealed record JoinRoomResult(
    RoomDetails Room,
    bool Joined);