using VibyRoom.Domain.Music.Tracks;

namespace VibyRoom.Application.Contracts.Music;

public sealed record PlaybackSource
{
    public PlaybackSource(
        TrackReference reference,
        Uri streamUrl,
        DateTimeOffset? expiresAtUtc,
        string? contentType)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(streamUrl);

        if (!streamUrl.IsAbsoluteUri ||
            streamUrl.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(streamUrl.UserInfo))
        {
            throw new ArgumentException(
                "Stream URL must be an absolute HTTPS URI.",
                nameof(streamUrl));
        }

        Reference = reference;
        StreamUrl = streamUrl;
        ExpiresAtUtc = expiresAtUtc?.ToUniversalTime();
        ContentType = contentType;
    }

    public TrackReference Reference { get; }

    public Uri StreamUrl { get; }

    public DateTimeOffset? ExpiresAtUtc { get; }

    public string? ContentType { get; }
}