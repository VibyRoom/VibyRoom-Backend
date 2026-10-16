namespace VibyRoom.Application.Abstractions.Realtime;

public sealed record RoomMemberRealtimeData(
    Guid GuestId,
    string DisplayName,
    DateTimeOffset JoinedAtUtc);