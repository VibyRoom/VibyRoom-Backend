using VibyRoom.Application.Contracts.Music;

namespace VibyRoom.Application.Abstractions.Music;

public interface IMusicCatalogProvider
{
    string ProviderId { get; }

    Task<IReadOnlyList<MusicTrack>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken = default);

    Task<MusicTrack?> GetByIdAsync(
        string trackId,
        CancellationToken cancellationToken = default);
}