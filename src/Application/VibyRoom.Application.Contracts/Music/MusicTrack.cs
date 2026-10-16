using VibyRoom.Domain.Music.Tracks;

namespace VibyRoom.Application.Contracts.Music;

public sealed record MusicTrack
{
    public MusicTrack(
        TrackReference reference,
        string title,
        IReadOnlyList<string> artists,
        string? album,
        long? durationMs,
        string? artworkUrl,
        string? externalUrl)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(artists);

        if (durationMs is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(durationMs),
                "Track duration must be positive when provided.");
        }

        var artistArray = artists.ToArray();

        if (artistArray.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Artist names cannot be null or whitespace.",
                nameof(artists));
        }

        Reference = reference;
        Title = title;
        Artists = Array.AsReadOnly(artistArray);
        Album = album;
        DurationMs = durationMs;
        ArtworkUrl = artworkUrl;
        ExternalUrl = externalUrl;
    }

    public TrackReference Reference { get; }

    public string Title { get; }

    public IReadOnlyList<string> Artists { get; }

    public string? Album { get; }

    public long? DurationMs { get; }

    public string? ArtworkUrl { get; }

    public string? ExternalUrl { get; }
}