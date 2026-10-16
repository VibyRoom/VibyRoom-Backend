
using VibyRoom.Domain.Music.Tracks;

namespace VibyRoom.Domain.Music.Queue;

public sealed class QueuedTrack : IEquatable<QueuedTrack>
{
    private const int MaxProviderLength = 32;
    private const int MaxTrackIdLength = 256;
    private const int MaxTitleLength = 300;

    private readonly IReadOnlyList<string> _artists;

    public QueuedTrack(
        TrackReference reference,
        string title,
        IReadOnlyList<string> artists,
        long durationMs,
        string? artworkUrl,
        string? externalUrl)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(artists);

        if (reference.Provider.Length > MaxProviderLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reference),
                "Music provider ID cannot exceed 32 characters.");
        }

        if (reference.TrackId.Length > MaxTrackIdLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reference),
                "Track ID cannot exceed 256 characters.");
        }

        var normalizedTitle = title.Trim();

        if (normalizedTitle.Length > MaxTitleLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(title),
                "Track title cannot exceed 300 characters.");
        }

        if (durationMs <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(durationMs),
                "Track duration must be greater than zero.");
        }

        var normalizedArtists = new string[artists.Count];

        for (var index = 0; index < artists.Count; index++)
        {
            var artist = artists[index];

            if (string.IsNullOrWhiteSpace(artist))
            {
                throw new ArgumentException(
                    "Artist names cannot be null or whitespace.",
                    nameof(artists));
            }

            normalizedArtists[index] = artist.Trim();
        }

        Reference = reference;
        Title = normalizedTitle;
        _artists = Array.AsReadOnly(normalizedArtists);
        DurationMs = durationMs;
        ArtworkUrl = ValidateOptionalUrl(
            artworkUrl,
            nameof(artworkUrl));
        ExternalUrl = ValidateOptionalUrl(
            externalUrl,
            nameof(externalUrl));
    }

    public TrackReference Reference { get; }

    public string Title { get; }

    public IReadOnlyList<string> Artists => _artists;

    public long DurationMs { get; }

    public string? ArtworkUrl { get; }

    public string? ExternalUrl { get; }

    public bool Equals(QueuedTrack? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null)
        {
            return false;
        }

        return Reference == other.Reference
            && string.Equals(
                Title,
                other.Title,
                StringComparison.Ordinal)
            && _artists.SequenceEqual(
                other._artists,
                StringComparer.Ordinal)
            && DurationMs == other.DurationMs
            && string.Equals(
                ArtworkUrl,
                other.ArtworkUrl,
                StringComparison.Ordinal)
            && string.Equals(
                ExternalUrl,
                other.ExternalUrl,
                StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is QueuedTrack other && Equals(other);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(Reference);
        hash.Add(Title, StringComparer.Ordinal);

        foreach (var artist in _artists)
        {
            hash.Add(artist, StringComparer.Ordinal);
        }

        hash.Add(DurationMs);
        hash.Add(ArtworkUrl, StringComparer.Ordinal);
        hash.Add(ExternalUrl, StringComparer.Ordinal);

        return hash.ToHashCode();
    }

    public static bool operator ==(
        QueuedTrack? left,
        QueuedTrack? right)
    {
        return Equals(left, right);
    }

    public static bool operator !=(
        QueuedTrack? left,
        QueuedTrack? right)
    {
        return !Equals(left, right);
    }

    private static string? ValidateOptionalUrl(
        string? value,
        string parameterName)
    {
        if (value is null)
        {
            return null;
        }

        if (!Uri.TryCreate(
                value,
                UriKind.Absolute,
                out var uri)
            || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "URL must be an absolute HTTPS address.",
                parameterName);
        }

        return value;
    }
}