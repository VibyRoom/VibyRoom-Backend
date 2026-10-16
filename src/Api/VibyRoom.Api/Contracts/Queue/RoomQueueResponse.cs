namespace VibyRoom.Api.Contracts.Queue;

public sealed record RoomQueueResponse(
    Guid RoomId,
    long Version,
    int MaxPendingItems,
    QueueItemResponse[] Items);