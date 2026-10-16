namespace VibyRoom.Application.Errors;

public enum ApplicationErrorCode
{
    GuestSessionRequired,
    Validation,
    RoomNotFound,
    InvalidInvitation,
    AccessDenied,
    MemberNotFound,
    NotMember,
    RoomFull,
    OwnerTransferRequired,
    RoomClosed,
    ConcurrencyConflict,
    InviteCodeConflict,
    TrackNotFound,
    UnsupportedMusicProvider,
    MusicProviderUnavailable,
    MusicProviderRateLimited,
    QueueFull,
    QueueItemNotFound,
    InvalidQueuePosition,
    TrackNotEligible,
    QueueVersionMismatch,
    QueuePreconditionRequired
}