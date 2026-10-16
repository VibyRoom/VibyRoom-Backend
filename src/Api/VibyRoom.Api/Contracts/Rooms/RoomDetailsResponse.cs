namespace VibyRoom.Api.Contracts.Rooms;

public sealed record RoomDetailsResponse(
    Guid Id,
    string Name,
    string Status,
    string InviteCode,
    int Capacity,
    IReadOnlyList<RoomMemberResponse> Members);