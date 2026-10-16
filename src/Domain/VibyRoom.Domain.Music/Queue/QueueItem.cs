using VibyRoom.Domain.Music.Tracks;

namespace VibyRoom.Domain.Music.Queue;

public sealed class QueueItem
{
    private QueuedTrack? _track;

    // Immutable track snapshot persisted as field-only properties.
    private string _provider = null!;
    private string _trackId = null!;
    private string _title = null!;
    private string[] _artists = [];
    private long _durationMs;
    private string? _artworkUrl;
    private string? _externalUrl;

    private QueueItem()
    {
        // EF Core materialization.
    }

    internal QueueItem(
        Guid id,
        QueuedTrack track,
        Guid addedByGuestId,
        DateTimeOffset addedAtUtc,
        int position)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Queue item ID cannot be empty.",
                nameof(id));
        }

        ArgumentNullException.ThrowIfNull(track);

        if (addedByGuestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Guest ID cannot be empty.",
                nameof(addedByGuestId));
        }

        if (addedAtUtc == default)
        {
            throw new ArgumentException(
                "Track addition time is required.",
                nameof(addedAtUtc));
        }

        if (position < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(position),
                "Queue item position cannot be negative.");
        }

        Id = id;
        AddedByGuestId = addedByGuestId;
        AddedAtUtc = addedAtUtc.ToUniversalTime();
        Position = position;

        _provider = track.Reference.Provider;
        _trackId = track.Reference.TrackId;
        _title = track.Title;
        _artists = track.Artists.ToArray();
        _durationMs = track.DurationMs;
        _artworkUrl = track.ArtworkUrl;
        _externalUrl = track.ExternalUrl;

        _track = track;
    }

    public Guid Id { get; private set; }

    public QueuedTrack Track => _track ??= new QueuedTrack(
        new TrackReference(_provider, _trackId),
        _title,
        _artists,
        _durationMs,
        _artworkUrl,
        _externalUrl);

    public Guid AddedByGuestId { get; private set; }

    public DateTimeOffset AddedAtUtc { get; private set; }

    public int Position { get; private set; }

    internal void ChangePosition(int position)
    {
        if (position < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(position),
                "Queue item position cannot be negative.");
        }

        Position = position;
    }
}