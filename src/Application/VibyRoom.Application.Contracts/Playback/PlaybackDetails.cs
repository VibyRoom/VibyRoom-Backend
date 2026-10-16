namespace VibyRoom.Application.Contracts.Playback;

public sealed record PlaybackDetails(
    Guid RoomId,
    long Version,
    string Status,
    CurrentPlaybackItemDetails? CurrentItem,
    long PositionMs,
    DateTimeOffset ServerTimeUtc,
    DateTimeOffset? AnchorAtUtc,
    long PositionAnchorMs);