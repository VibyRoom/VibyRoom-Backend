using VibyRoom.Application.Contracts.Queue;

namespace VibyRoom.Application.Contracts.Playback;

public sealed record CurrentPlaybackItemDetails(
    Guid Id,
    QueuedTrackDetails Track,
    Guid AddedByGuestId,
    DateTimeOffset AddedAtUtc);