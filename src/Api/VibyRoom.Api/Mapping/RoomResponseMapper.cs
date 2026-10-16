using VibyRoom.Api.Contracts.Rooms;
using VibyRoom.Application.Contracts.Rooms;
using VibyRoom.Domain.Core.Entities.Rooms.Enums;

namespace VibyRoom.Api.Mapping;

public static class RoomResponseMapper
{
    public static RoomDetailsResponse ToResponse(
        RoomDetails room)
    {
        ArgumentNullException.ThrowIfNull(room);

        var members = room.Members
            .Select(ToResponse)
            .ToArray();

        return new RoomDetailsResponse(
            room.Id,
            room.Name,
            MapStatus(room.Status),
            room.InviteCode,
            room.Capacity,
            Array.AsReadOnly(members));
    }

    public static RoomMemberResponse ToResponse(
        RoomMemberDetails member)
    {
        ArgumentNullException.ThrowIfNull(member);

        return new RoomMemberResponse(
            member.GuestId,
            member.DisplayName,
            MapRole(member.Role),
            member.JoinedAtUtc);
    }

    public static RoomPreviewResponse ToResponse(
        RoomPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        return new RoomPreviewResponse(
            preview.Id,
            preview.Name);
    }

    private static string MapStatus(RoomStatus status)
    {
        return status switch
        {
            RoomStatus.Active => "active",
            RoomStatus.Closed => "closed",
            _ => throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "Unsupported room status.")
        };
    }

    private static string MapRole(RoomRole role)
    {
        return role switch
        {
            RoomRole.Owner => "owner",
            RoomRole.Member => "member",
            _ => throw new ArgumentOutOfRangeException(
                nameof(role),
                role,
                "Unsupported room role.")
        };
    }
}