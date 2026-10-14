namespace VibyRoom.Api.Contracts.Rooms;

public sealed record RoomMemberResponse(
    Guid GuestId,
    string DisplayName,
    string Role,
    DateTimeOffset JoinedAtUtc);