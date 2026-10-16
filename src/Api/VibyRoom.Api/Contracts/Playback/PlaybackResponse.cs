namespace VibyRoom.Api.Contracts.Playback;

public sealed record PlaybackResponse(
    Guid RoomId,
    long Version,
    string Status,
    CurrentPlaybackItemResponse? CurrentItem,
    long PositionMs,
    DateTimeOffset ServerTimeUtc,
    DateTimeOffset? AnchorAtUtc,
    long PositionAnchorMs);