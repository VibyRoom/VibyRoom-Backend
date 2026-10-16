namespace VibyRoom.Domain.Music.Tracks;

public sealed record TrackReference
{
    public TrackReference(string provider, string trackId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(trackId);

        Provider = provider;
        TrackId = trackId;
    }

    public string Provider { get; }

    public string TrackId { get; }
}