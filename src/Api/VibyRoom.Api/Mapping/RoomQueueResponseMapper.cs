using VibyRoom.Api.Contracts.Music;
using VibyRoom.Api.Contracts.Queue;
using VibyRoom.Application.Contracts.Queue;

namespace VibyRoom.Api.Mapping;

public static class RoomQueueResponseMapper
{
    public static RoomQueueResponse ToResponse(
        RoomQueueDetails queue)
    {
        ArgumentNullException.ThrowIfNull(queue);

        var items = queue.Items
            .Select(ToItemResponse)
            .ToArray();

        return new RoomQueueResponse(
            queue.RoomId,
            queue.Version,
            queue.MaxPendingItems,
            items);
    }

    private static QueueItemResponse ToItemResponse(
        QueueItemDetails item)
    {
        var track = item.Track;

        var reference = new TrackReferenceResponse(
            track.Reference.Provider,
            track.Reference.TrackId);

        var trackResponse = new QueuedTrackResponse(
            reference,
            track.Title,
            track.Artists.ToArray(),
            track.DurationMs,
            track.ArtworkUrl,
            track.ExternalUrl);

        return new QueueItemResponse(
            item.Id,
            item.Position,
            trackResponse,
            item.AddedByGuestId,
            item.AddedAtUtc);
    }
}