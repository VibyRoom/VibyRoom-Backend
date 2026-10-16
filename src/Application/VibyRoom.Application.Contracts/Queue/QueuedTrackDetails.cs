using VibyRoom.Domain.Music.Tracks;

namespace VibyRoom.Application.Contracts.Queue;

public sealed record QueuedTrackDetails
{
    public QueuedTrackDetails(
        TrackReference reference,
        string title,
        IReadOnlyList<string> artists,
        long durationMs,
        string? artworkUrl,
        string? externalUrl)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(artists);

        Reference = reference;
        Title = title;
        Artists = Array.AsReadOnly(artists.ToArray());
        DurationMs = durationMs;
        ArtworkUrl = artworkUrl;
        ExternalUrl = externalUrl;
    }

    public TrackReference Reference { get; }
    public string Title { get; }
    public IReadOnlyList<string> Artists { get; }
    public long DurationMs { get; }
    public string? ArtworkUrl { get; }
    public string? ExternalUrl { get; }
}