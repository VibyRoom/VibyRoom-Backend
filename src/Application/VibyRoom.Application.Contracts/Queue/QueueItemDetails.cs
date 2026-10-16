namespace VibyRoom.Application.Contracts.Queue;

public sealed record QueueItemDetails(
    Guid Id,
    int Position,
    QueuedTrackDetails Track,
    Guid AddedByGuestId,
    DateTimeOffset AddedAtUtc);