namespace VibyRoom.Api.Background;

public sealed class PlaybackProgressionOptions
{
    public const string SectionName = "PlaybackProgression";

    public int IntervalMilliseconds { get; set; } = 1000;

    public int BatchSize { get; set; } = 100;
}