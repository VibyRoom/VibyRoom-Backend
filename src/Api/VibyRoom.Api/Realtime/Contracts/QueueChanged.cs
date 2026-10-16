using VibyRoom.Application.Contracts.Queue;

namespace VibyRoom.Api.Realtime.Contracts;

public sealed record QueueChanged(
    Guid RoomId,
    long Version,
    IReadOnlyList<QueueItemDetails> Items);