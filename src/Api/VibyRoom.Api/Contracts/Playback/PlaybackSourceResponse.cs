using VibyRoom.Api.Contracts.Music;

namespace VibyRoom.Api.Contracts.Playback;

public sealed record PlaybackSourceResponse(
    TrackReferenceResponse Reference,
    string StreamUrl,
    DateTimeOffset? ExpiresAtUtc,
    string? ContentType);