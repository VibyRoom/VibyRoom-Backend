namespace VibyRoom.Application.Contracts.Rooms;

public sealed record CreateRoomCommand(
    string Name,
    string DisplayName);