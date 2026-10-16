using VibyRoom.Application.Contracts.Playback;
using VibyRoom.Application.Contracts.Queue;
using VibyRoom.Domain.Music.Playback;
using VibyRoom.Domain.Music.Queue;

namespace VibyRoom.Application.Playback;

public static class RoomMusicStateMapper
{
    public static RoomMusicStateDetails ToDetails(
        Guid roomId,
        RoomMusicSession? session,
        DateTimeOffset serverTimeUtc)
    {
        var version = session?.Version ?? 0;

        var queue = session?.PendingItems
            .OrderBy(item => item.Position)
            .Select(MapQueueItem)
            .ToArray()
            ?? [];

        var playback = session?.Playback;

        var currentItem = playback?.CurrentItem is { } item
            ? new CurrentPlaybackItemDetails(
                item.Id,
                MapTrack(item.Track),
                item.AddedByGuestId,
                item.AddedAtUtc)
            : null;

        var playbackDetails = new PlaybackDetails(
            roomId,
            version,
            (playback?.Status ?? PlaybackStatus.Stopped)
                .ToString()
                .ToLowerInvariant(),
            currentItem,
            session?.GetCurrentPosition(serverTimeUtc) ?? 0,
            serverTimeUtc,
            playback?.AnchorAtUtc,
            playback?.PositionAnchorMs ?? 0);

        return new RoomMusicStateDetails(
            roomId,
            version,
            queue,
            playbackDetails,
            serverTimeUtc);
    }

    private static QueueItemDetails MapQueueItem(
        QueueItem item)
    {
        return new QueueItemDetails(
            item.Id,
            item.Position,
            MapTrack(item.Track),
            item.AddedByGuestId,
            item.AddedAtUtc);
    }

    private static QueuedTrackDetails MapTrack(
        QueuedTrack track)
    {
        return new QueuedTrackDetails(
            track.Reference,
            track.Title,
            track.Artists,
            track.DurationMs,
            track.ArtworkUrl,
            track.ExternalUrl);
    }
}