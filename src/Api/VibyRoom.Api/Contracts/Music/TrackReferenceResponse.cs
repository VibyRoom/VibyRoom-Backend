namespace VibyRoom.Api.Contracts.Music;

public sealed record TrackReferenceResponse(
    string Provider,
    string TrackId);