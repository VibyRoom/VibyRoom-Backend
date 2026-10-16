namespace VibyRoom.Domain.Music.Playback;

public sealed class PlaybackState
{
    private PlaybackState()
    {
        Status = PlaybackStatus.Stopped;
    }

    public PlaybackStatus Status { get; private set; }
    public CurrentPlaybackItem? CurrentItem { get; private set; }
    public long PositionAnchorMs { get; private set; }
    public DateTimeOffset? AnchorAtUtc { get; private set; }

    internal static PlaybackState Stopped() => new();

    internal void Start(
        CurrentPlaybackItem item,
        DateTimeOffset nowUtc,
        bool paused)
    {
        ArgumentNullException.ThrowIfNull(item);
        EnsureTimestamp(nowUtc);

        CurrentItem = item;
        PositionAnchorMs = 0;
        AnchorAtUtc = paused
            ? null
            : nowUtc.ToUniversalTime();

        Status = paused
            ? PlaybackStatus.Paused
            : PlaybackStatus.Playing;
    }

    internal void Pause(DateTimeOffset nowUtc)
    {
        PositionAnchorMs = GetCurrentPosition(nowUtc);
        AnchorAtUtc = null;
        Status = PlaybackStatus.Paused;
    }

    internal void Resume(DateTimeOffset nowUtc)
    {
        EnsureTimestamp(nowUtc);
        AnchorAtUtc = nowUtc.ToUniversalTime();
        Status = PlaybackStatus.Playing;
    }

    internal void Seek(
        long positionMs,
        DateTimeOffset nowUtc)
    {
        EnsureTimestamp(nowUtc);
        PositionAnchorMs = positionMs;

        if (Status == PlaybackStatus.Playing)
        {
            AnchorAtUtc = nowUtc.ToUniversalTime();
        }
    }

    internal void Stop()
    {
        Status = PlaybackStatus.Stopped;
        CurrentItem = null;
        PositionAnchorMs = 0;
        AnchorAtUtc = null;
    }

    public long GetCurrentPosition(DateTimeOffset nowUtc)
    {
        EnsureTimestamp(nowUtc);

        if (Status == PlaybackStatus.Stopped ||
            CurrentItem is null)
        {
            return 0;
        }

        var durationMs = CurrentItem.Track.DurationMs;
        var position = Math.Min(
            PositionAnchorMs, durationMs);

        if (Status == PlaybackStatus.Paused ||
            AnchorAtUtc is null)
        {
            return position;
        }

        var elapsed =
            nowUtc.ToUniversalTime() - AnchorAtUtc.Value;

        if (elapsed <= TimeSpan.Zero)
        {
            return position;
        }

        var remainingMs = durationMs - position;
        var wholeElapsedMs =
            elapsed.Ticks / TimeSpan.TicksPerMillisecond;

        return wholeElapsedMs >= remainingMs
            ? durationMs
            : position + wholeElapsedMs;
    }

    internal DateTimeOffset GetScheduledEndUtc()
    {
        if (Status != PlaybackStatus.Playing ||
            CurrentItem is null ||
            AnchorAtUtc is null)
        {
            throw new InvalidOperationException(
                "No playing track is available.");
        }

        var remainingMs =
            CurrentItem.Track.DurationMs - PositionAnchorMs;

        var anchor = AnchorAtUtc.Value.ToUniversalTime();

        var availableTicks =
            DateTimeOffset.MaxValue.UtcTicks -
            anchor.UtcTicks;

        if (remainingMs >
            availableTicks / TimeSpan.TicksPerMillisecond)
        {
            return DateTimeOffset.MaxValue;
        }

        return anchor.AddTicks(
            remainingMs * TimeSpan.TicksPerMillisecond);
    }

    private static void EnsureTimestamp(
        DateTimeOffset value)
    {
        if (value == default)
        {
            throw new ArgumentException(
                "Server time is required.", nameof(value));
        }
    }
}