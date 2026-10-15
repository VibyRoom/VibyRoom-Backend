namespace VibyRoom.Api.Contracts.Music;

public sealed record MusicTrackResponse(
    TrackReferenceResponse Reference,
    string Title,
    string[] Artists,
    string? Album,
    long? DurationMs,
    string? ArtworkUrl,
    string? ExternalUrl);