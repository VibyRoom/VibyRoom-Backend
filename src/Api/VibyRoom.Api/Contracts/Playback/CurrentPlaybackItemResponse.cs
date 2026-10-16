using VibyRoom.Api.Contracts.Queue;

namespace VibyRoom.Api.Contracts.Playback;

public sealed record CurrentPlaybackItemResponse(
    Guid Id,
    QueuedTrackResponse Track,
    Guid AddedByGuestId,
    DateTimeOffset AddedAtUtc);