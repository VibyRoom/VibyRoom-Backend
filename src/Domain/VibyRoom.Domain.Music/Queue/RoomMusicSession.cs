
using VibyRoom.Domain.Core.Entities.Rooms.Enums;
using VibyRoom.Domain.Music.Queue.Exceptions;

namespace VibyRoom.Domain.Music.Queue;

public sealed class RoomMusicSession
{
    private readonly List<QueueItem> _pendingItems = [];
    private readonly IReadOnlyList<QueueItem> _pendingItemsView;

    private RoomMusicSession()
    {
        _pendingItemsView = _pendingItems.AsReadOnly();
    }

    private RoomMusicSession(
        Guid roomId,
        int maxPendingItems)
        : this()
    {
        if (roomId == Guid.Empty)
        {
            throw new ArgumentException(
                "Room ID cannot be empty.",
                nameof(roomId));
        }

        if (maxPendingItems <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxPendingItems),
                "Maximum queue size must be positive.");
        }

        RoomId = roomId;
        MaxPendingItems = maxPendingItems;
        Version = 0;
    }

    public Guid RoomId { get; private set; }

    public long Version { get; private set; }

    public int MaxPendingItems { get; private set; }

    public IReadOnlyList<QueueItem> PendingItems =>
        _pendingItemsView;

    public static RoomMusicSession Create(
        Guid roomId,
        int maxPendingItems)
    {
        return new RoomMusicSession(
            roomId,
            maxPendingItems);
    }

    public QueueItem AddTrack(
        QueuedTrack track,
        Guid guestId,
        DateTimeOffset addedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(track);

        if (guestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Guest ID cannot be empty.",
                nameof(guestId));
        }

        if (addedAtUtc == default)
        {
            throw new ArgumentException(
                "Track addition time is required.",
                nameof(addedAtUtc));
        }

        if (_pendingItems.Count >= MaxPendingItems)
        {
            throw new RoomQueueDomainException(
                RoomQueueDomainErrorCode.QueueFull,
                "The music queue has reached its maximum size.");
        }

        EnsureVersionCanAdvance();

        var item = new QueueItem(
            Guid.NewGuid(),
            track,
            guestId,
            addedAtUtc,
            _pendingItems.Count);

        _pendingItems.Add(item);
        Version++;

        return item;
    }

    public void RemoveTrack(
        Guid itemId,
        Guid actorGuestId,
        RoomRole actorRole)
    {
        ValidateItemId(itemId);

        if (actorGuestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Actor guest ID cannot be empty.",
                nameof(actorGuestId));
        }

        EnsureValidRole(actorRole);

        var index = FindItemIndex(itemId);

        if (index < 0)
        {
            throw new RoomQueueDomainException(
                RoomQueueDomainErrorCode.QueueItemNotFound,
                "The requested queue item was not found.");
        }

        var item = _pendingItems[index];

        if (actorRole != RoomRole.Owner &&
            item.AddedByGuestId != actorGuestId)
        {
            throw new RoomQueueDomainException(
                RoomQueueDomainErrorCode.AccessDenied,
                "You can only remove your own queue items.");
        }

        EnsureVersionCanAdvance();

        _pendingItems.RemoveAt(index);

        NormalizePositions();

        Version++;
    }

    public bool MoveTrack(
        Guid itemId,
        int newPosition,
        RoomRole actorRole)
    {
        ValidateItemId(itemId);

        if (actorRole != RoomRole.Owner)
        {
            throw new RoomQueueDomainException(
                RoomQueueDomainErrorCode.AccessDenied,
                "Only the room owner can reorder the music queue.");
        }

        var currentIndex = FindItemIndex(itemId);

        if (currentIndex < 0)
        {
            throw new RoomQueueDomainException(
                RoomQueueDomainErrorCode.QueueItemNotFound,
                "The requested queue item was not found.");
        }

        if (newPosition < 0 ||
            newPosition >= _pendingItems.Count)
        {
            throw new RoomQueueDomainException(
                RoomQueueDomainErrorCode.InvalidQueuePosition,
                "The requested queue position is invalid.");
        }

        if (currentIndex == newPosition)
        {
            return false;
        }

        EnsureVersionCanAdvance();

        var item = _pendingItems[currentIndex];

        _pendingItems.RemoveAt(currentIndex);
        _pendingItems.Insert(newPosition, item);

        NormalizePositions();

        Version++;

        return true;
    }

    public QueueItem? FindItem(Guid itemId)
    {
        if (itemId == Guid.Empty)
        {
            return null;
        }

        return _pendingItems.FirstOrDefault(
            item => item.Id == itemId);
    }

    private int FindItemIndex(Guid itemId)
    {
        return _pendingItems.FindIndex(
            item => item.Id == itemId);
    }

    private void NormalizePositions()
    {
        for (var index = 0;
             index < _pendingItems.Count;
             index++)
        {
            _pendingItems[index].ChangePosition(index);
        }
    }

    private void EnsureVersionCanAdvance()
    {
        if (Version == long.MaxValue)
        {
            throw new InvalidOperationException(
                "Room music session version has reached its maximum value.");
        }
    }

    private static void ValidateItemId(Guid itemId)
    {
        if (itemId == Guid.Empty)
        {
            throw new ArgumentException(
                "Queue item ID cannot be empty.",
                nameof(itemId));
        }
    }

    private static void EnsureValidRole(RoomRole actorRole)
    {
        if (actorRole is not
            (RoomRole.Owner or RoomRole.Member))
        {
            throw new RoomQueueDomainException(
                RoomQueueDomainErrorCode.AccessDenied,
                "The specified room role is not permitted.");
        }
    }
}
