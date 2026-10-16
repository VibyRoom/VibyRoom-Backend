namespace VibyRoom.Application.Contracts.Rooms;

public sealed record JoinRoomCommand(
    string InviteCode,
    string DisplayName);