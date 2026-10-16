using VibyRoom.Domain.Music.Playback;

namespace VibyRoom.Infrastructure.Persistence.Repositories;

internal static class PlaybackTransitionCalculator
{
    // Projection used only for the indexed PostgreSQL due-time column.
    // Actual playback is always derived from the domain object's anchors.
    public static DateTimeOffset? Calculate(PlaybackState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.Status != PlaybackStatus.Playing)
        {
            return null;
        }

        var current = state.CurrentItem
                      ?? throw new InvalidOperationException("Playing requires a current track.");

        var anchor = state.AnchorAtUtc
                     ?? throw new InvalidOperationException("Playing requires a time anchor.");

        var remainingMs = current.Track.DurationMs - state.PositionAnchorMs;

        if (remainingMs <= 0)
        {
            return anchor.ToUniversalTime();
        }

        var utcAnchor = anchor.ToUniversalTime();
        var maxTicks = DateTimeOffset.MaxValue.UtcTicks - utcAnchor.UtcTicks;

        if (remainingMs > maxTicks / TimeSpan.TicksPerMillisecond)
        {
            return DateTimeOffset.MaxValue;
        }

        return utcAnchor.AddTicks(remainingMs * TimeSpan.TicksPerMillisecond);
    }
}