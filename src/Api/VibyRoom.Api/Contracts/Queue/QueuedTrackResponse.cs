using VibyRoom.Api.Contracts.Music;

namespace VibyRoom.Api.Contracts.Queue;

public sealed record QueuedTrackResponse(
    TrackReferenceResponse Reference,
    string Title,
    string[] Artists,
    long DurationMs,
    string? ArtworkUrl,
    string? ExternalUrl);