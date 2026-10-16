namespace VibyRoom.Application.Contracts.Queue;

public sealed record RoomQueueDetails
{
    public RoomQueueDetails(
        Guid roomId,
        long version,
        int maxPendingItems,
        IReadOnlyList<QueueItemDetails> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        RoomId = roomId;
        Version = version;
        MaxPendingItems = maxPendingItems;
        Items = Array.AsReadOnly(items.ToArray());
    }

    public Guid RoomId { get; }
    public long Version { get; }
    public int MaxPendingItems { get; }
    public IReadOnlyList<QueueItemDetails> Items { get; }
}