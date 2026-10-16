namespace VibyRoom.Application.Configuration;

public sealed class RoomQueueSettings
{
    public const string SectionName = "RoomQueue";

    public int MaxPendingItems { get; init; } = 100;
}