namespace VibyRoom.Application.Configuration;

public sealed class RoomSettings
{
    public const string SectionName = "Rooms";

    public int Capacity { get; init; }
}