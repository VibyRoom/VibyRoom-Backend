using VibyRoom.Domain.Music.Queue;

namespace VibyRoom.Domain.Music.Playback;

public sealed record CurrentPlaybackItem
{
    public CurrentPlaybackItem(
        Guid id,
        QueuedTrack track,
        Guid addedByGuestId,
        DateTimeOffset addedAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Item ID is required.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(track);

        if (addedByGuestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Guest ID is required.", nameof(addedByGuestId));
        }

        if (addedAtUtc == default)
        {
            throw new ArgumentException(
                "Addition time is required.", nameof(addedAtUtc));
        }

        Id = id;
        Track = track;
        AddedByGuestId = addedByGuestId;
        AddedAtUtc = addedAtUtc.ToUniversalTime();
    }

    public Guid Id { get; }
    public QueuedTrack Track { get; }
    public Guid AddedByGuestId { get; }
    public DateTimeOffset AddedAtUtc { get; }

    internal static CurrentPlaybackItem FromQueueItem(
        QueueItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new CurrentPlaybackItem(
            item.Id,
            item.Track,
            item.AddedByGuestId,
            item.AddedAtUtc);
    }
}