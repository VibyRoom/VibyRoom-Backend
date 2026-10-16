using VibyRoom.Api.Contracts.Music;
using VibyRoom.Api.Contracts.Playback;
using VibyRoom.Api.Contracts.Queue;
using VibyRoom.Application.Contracts.Playback;
using VibyRoom.Application.Contracts.Queue;

namespace VibyRoom.Api.Mapping;

public static class RoomMusicStateResponseMapper
{
    public static RoomMusicStateResponse ToResponse(
        RoomMusicStateDetails state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var queue = state.Queue
            .OrderBy(item => item.Position)
            .Select(MapQueueItem)
            .ToArray();

        return new RoomMusicStateResponse(
            state.RoomId,
            state.Version,
            queue,
            ToPlaybackResponse(state.Playback),
            state.ServerTimeUtc);
    }

    public static PlaybackResponse ToPlaybackResponse(
        PlaybackDetails playback)
    {
        ArgumentNullException.ThrowIfNull(playback);

        CurrentPlaybackItemResponse? currentItem = null;

        if (playback.CurrentItem is { } item)
        {
            currentItem = new CurrentPlaybackItemResponse(
                item.Id,
                MapTrack(item.Track),
                item.AddedByGuestId,
                item.AddedAtUtc);
        }

        return new PlaybackResponse(
            playback.RoomId,
            playback.Version,
            playback.Status,
            currentItem,
            playback.PositionMs,
            playback.ServerTimeUtc,
            playback.AnchorAtUtc,
            playback.PositionAnchorMs);
    }

    private static QueueItemResponse MapQueueItem(
        QueueItemDetails item)
    {
        return new QueueItemResponse(
            item.Id,
            item.Position,
            MapTrack(item.Track),
            item.AddedByGuestId,
            item.AddedAtUtc);
    }

    private static QueuedTrackResponse MapTrack(
        QueuedTrackDetails track)
    {
        var reference = new TrackReferenceResponse(
            track.Reference.Provider,
            track.Reference.TrackId);

        return new QueuedTrackResponse(
            reference,
            track.Title,
            track.Artists.ToArray(),
            track.DurationMs,
            track.ArtworkUrl,
            track.ExternalUrl);
    }
}