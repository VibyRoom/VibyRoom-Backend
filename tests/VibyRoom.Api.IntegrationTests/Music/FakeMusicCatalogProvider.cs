using VibyRoom.Application.Abstractions.Music;
using VibyRoom.Application.Contracts.Music;

namespace VibyRoom.Api.IntegrationTests.Music;

public sealed class FakeMusicCatalogProvider : IMusicCatalogProvider
{
    public string ProviderId => "audius";

    public string? LastSearchQuery { get; private set; }

    public int? LastSearchLimit { get; private set; }

    public string? LastTrackId { get; private set; }

    public CancellationToken LastCancellationToken { get; private set; }

    public int SearchCalls { get; private set; }

    public int DetailsCalls { get; private set; }

    public Func<string, int, CancellationToken,
        Task<IReadOnlyList<MusicTrack>>> SearchBehavior { get; set; } =
        (_, _, _) => Task.FromResult<IReadOnlyList<MusicTrack>>(
            Array.Empty<MusicTrack>());

    public Func<string, CancellationToken,
        Task<MusicTrack?>> DetailsBehavior { get; set; } =
        (_, _) => Task.FromResult<MusicTrack?>(null);

    public Task<IReadOnlyList<MusicTrack>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        SearchCalls++;
        LastSearchQuery = query;
        LastSearchLimit = limit;
        LastCancellationToken = cancellationToken;

        return SearchBehavior(query, limit, cancellationToken);
    }

    public Task<MusicTrack?> GetByIdAsync(
        string trackId,
        CancellationToken cancellationToken = default)
    {
        DetailsCalls++;
        LastTrackId = trackId;
        LastCancellationToken = cancellationToken;

        return DetailsBehavior(trackId, cancellationToken);
    }
}