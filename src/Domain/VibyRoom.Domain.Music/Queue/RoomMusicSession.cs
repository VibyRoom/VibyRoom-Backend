using System.Text.Json;
using VibyRoom.Domain.Core.Entities.Rooms.Enums;
using VibyRoom.Domain.Music.Playback;
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
        int maxPendingItems) : this()
    {
        if (roomId == Guid.Empty)
        {
            throw new ArgumentException(
                "Room ID cannot be empty.", nameof(roomId));
        }

        if (maxPendingItems <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxPendingItems));
        }

        RoomId = roomId;
        MaxPendingItems = maxPendingItems;
        Playback = PlaybackState.Stopped();
    }

    public Guid RoomId { get; private set; }

    public long Version { get; private set; }

    public int MaxPendingItems { get; private set; }

    public PlaybackState Playback { get; private set; }
        = PlaybackState.Stopped();

    public IReadOnlyList<QueueItem> PendingItems => _pendingItemsView;

    public string? AiPrompt { get; private set; }

    public string AiHistoryJson { get; private set; } = "[]";

    public IReadOnlyList<string> AiTrackKeys =>
        JsonSerializer.Deserialize<string[]>(AiHistoryJson) ?? [];

    public static RoomMusicSession Create(
        Guid roomId,
        int maxPendingItems)
    {
        return new RoomMusicSession(roomId, maxPendingItems);
    }

    public void RecordAiSelection(
        string prompt,
        IReadOnlyList<QueuedTrack> tracks,
        bool resetHistory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(tracks);

        if (tracks.Count != 15)
        {
            throw new ArgumentException(
                "AI selection must contain exactly 15 tracks.",
                nameof(tracks));
        }

        var history = resetHistory
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(
                AiTrackKeys,
                StringComparer.Ordinal);

        foreach (var track in tracks)
        {
            var key = $"{track.Reference.Provider}:{track.Reference.TrackId}";

            if (!history.Add(key))
            {
                throw new InvalidOperationException(
                    "AI selection contains a previously selected track.");
            }
        }

        AiPrompt = prompt;
        AiHistoryJson = JsonSerializer.Serialize(history);
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
                "Guest ID cannot be empty.", nameof(guestId));
        }

        if (addedAtUtc == default)
        {
            throw new ArgumentException(
                "Track addition time is required.", nameof(addedAtUtc));
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

    public void Play(DateTimeOffset nowUtc)
    {
        EnsureStatus(PlaybackStatus.Stopped);
        EnsureQueueNotEmpty();
        EnsureVersionCanAdvance();
        ValidateTime(nowUtc);

        StartNext(nowUtc, paused: false);
        Version++;
    }

    public void Pause(DateTimeOffset nowUtc)
    {
        EnsureStatus(PlaybackStatus.Playing);
        EnsureVersionCanAdvance();
        ValidateTime(nowUtc);

        if (Playback.GetCurrentPosition(nowUtc) >=
            Playback.CurrentItem!.Track.DurationMs)
        {
            throw new RoomQueueDomainException(
                RoomQueueDomainErrorCode.InvalidPlaybackState,
                "The track has already ended; advance playback first.");
        }

        Playback.Pause(nowUtc);
        Version++;
    }

    public void Resume(DateTimeOffset nowUtc)
    {
        EnsureStatus(PlaybackStatus.Paused);
        EnsureVersionCanAdvance();
        ValidateTime(nowUtc);

        Playback.Resume(nowUtc);
        Version++;
    }

    public bool Seek(long positionMs, DateTimeOffset nowUtc)
    {
        EnsureActivePlayback();
        ValidateTime(nowUtc);

        if (positionMs < 0 ||
            positionMs >= Playback.CurrentItem!.Track.DurationMs)
        {
            throw new RoomQueueDomainException(
                RoomQueueDomainErrorCode.InvalidPlaybackPosition,
                "The requested playback position is invalid.");
        }

        if (Playback.Status == PlaybackStatus.Playing &&
            Playback.GetCurrentPosition(nowUtc) >=
            Playback.CurrentItem.Track.DurationMs)
        {
            throw new RoomQueueDomainException(
                RoomQueueDomainErrorCode.InvalidPlaybackState,
                "The track has already ended; advance playback first.");
        }

        if (Playback.Status == PlaybackStatus.Paused &&
            Playback.PositionAnchorMs == positionMs)
        {
            return false;
        }

        EnsureVersionCanAdvance();

        Playback.Seek(positionMs, nowUtc);
        Version++;

        return true;
    }

    public void Skip(DateTimeOffset nowUtc)
    {
        EnsureActivePlayback();
        EnsureVersionCanAdvance();
        ValidateTime(nowUtc);

        var paused = Playback.Status == PlaybackStatus.Paused;

        if (_pendingItems.Count == 0)
        {
            Playback.Stop();
        }
        else
        {
            StartNext(nowUtc, paused);
        }

        Version++;
    }

    public bool AdvanceIfDue(DateTimeOffset nowUtc)
    {
        ValidateTime(nowUtc);

        if (Playback.Status != PlaybackStatus.Playing ||
            Playback.CurrentItem is null ||
            nowUtc.ToUniversalTime() < Playback.GetScheduledEndUtc())
        {
            return false;
        }

        EnsureVersionCanAdvance();

        do
        {
            var scheduledEnd = Playback.GetScheduledEndUtc();

            if (_pendingItems.Count == 0)
            {
                Playback.Stop();
                break;
            }

            StartNext(scheduledEnd, paused: false);
        }
        while (
            Playback.Status == PlaybackStatus.Playing &&
            nowUtc.ToUniversalTime() >= Playback.GetScheduledEndUtc());

        Version++;
        return true;
    }

    public long GetCurrentPosition(DateTimeOffset nowUtc)
    {
        return Playback.GetCurrentPosition(nowUtc);
    }

    public bool StopForRoomClosure()
    {
        if (Playback.Status == PlaybackStatus.Stopped)
        {
            return false;
        }

        EnsureVersionCanAdvance();

        Playback.Stop();
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

    private void StartNext(DateTimeOffset nowUtc, bool paused)
    {
        var queueItem = _pendingItems[0];
        var currentItem = CurrentPlaybackItem.FromQueueItem(queueItem);

        Playback.Start(currentItem, nowUtc, paused);

        _pendingItems.RemoveAt(0);
        NormalizePositions();
    }

    private void EnsureQueueNotEmpty()
    {
        if (_pendingItems.Count == 0)
        {
            throw new RoomQueueDomainException(
                RoomQueueDomainErrorCode.QueueEmpty,
                "The music queue is empty.");
        }
    }

    private void EnsureStatus(PlaybackStatus status)
    {
        if (Playback.Status != status)
        {
            throw new RoomQueueDomainException(
                RoomQueueDomainErrorCode.InvalidPlaybackState,
                $"Playback must be {status} for this operation.");
        }
    }

    private void EnsureActivePlayback()
    {
        if (Playback.Status == PlaybackStatus.Stopped ||
            Playback.CurrentItem is null)
        {
            throw new RoomQueueDomainException(
                RoomQueueDomainErrorCode.PlaybackNotActive,
                "No track is currently active.");
        }
    }

    private static void ValidateTime(DateTimeOffset nowUtc)
    {
        if (nowUtc == default)
        {
            throw new ArgumentException(
                "Server time is required.", nameof(nowUtc));
        }
    }

    private int FindItemIndex(Guid itemId)
    {
        return _pendingItems.FindIndex(item => item.Id == itemId);
    }

    private void NormalizePositions()
    {
        for (var index = 0; index < _pendingItems.Count; index++)
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
                "Queue item ID cannot be empty.", nameof(itemId));
        }
    }

    private static void EnsureValidRole(RoomRole actorRole)
    {
        if (actorRole is not (RoomRole.Owner or RoomRole.Member))
        {
            throw new RoomQueueDomainException(
                RoomQueueDomainErrorCode.AccessDenied,
                "The specified room role is not permitted.");
        }
    }
}