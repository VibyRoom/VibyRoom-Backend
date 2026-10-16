namespace VibyRoom.Api.Contracts.Queue;

public sealed record QueueItemResponse(
    Guid Id,
    int Position,
    QueuedTrackResponse Track,
    Guid AddedByGuestId,
    DateTimeOffset AddedAtUtc);