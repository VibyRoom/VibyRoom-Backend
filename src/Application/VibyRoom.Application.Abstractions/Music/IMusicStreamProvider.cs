using VibyRoom.Application.Contracts.Music;
using VibyRoom.Domain.Music.Tracks;

namespace VibyRoom.Application.Abstractions.Music;

public interface IMusicStreamProvider
{
    Task<PlaybackSource?> GetStreamSourceAsync(
        TrackReference reference,
        CancellationToken cancellationToken = default);

    Task<bool> CanStreamAsync(
        TrackReference reference,
        CancellationToken cancellationToken = default);
}