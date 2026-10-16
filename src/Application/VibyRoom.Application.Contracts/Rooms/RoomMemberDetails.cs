using VibyRoom.Domain.Core.Entities.Rooms.Enums;

namespace VibyRoom.Application.Contracts.Rooms;

public sealed record RoomMemberDetails(
    Guid GuestId,
    string DisplayName,
    RoomRole Role,
    DateTimeOffset JoinedAtUtc);