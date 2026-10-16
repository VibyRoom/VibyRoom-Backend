namespace VibyRoom.Domain.Core.Entities.Rooms.Exceprions;

public enum RoomDomainErrorCode
{
    RoomClosed,
    RoomFull,
    InvalidInviteCode,
    MemberNotFound,
    InsufficientPermissions,
    OwnershipTransferRequired,
    InvalidOperation
}