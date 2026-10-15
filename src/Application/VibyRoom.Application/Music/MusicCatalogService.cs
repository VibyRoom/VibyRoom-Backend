using VibyRoom.Application.Abstractions.Exceptions;
using VibyRoom.Application.Abstractions.Music;
using VibyRoom.Application.Contracts.Music;
using VibyRoom.Application.Errors;
using VibyRoom.Domain.Music.Tracks;

namespace VibyRoom.Application.Music;

public sealed class MusicCatalogService
{
    private const int MinQueryLength = 2;
    private const int MaxQueryLength = 100;
    private const int MinSearchLimit = 1;
    private const int MaxSearchLimit = 50;

    private readonly IMusicCatalogProvider _provider;

    public MusicCatalogService(IMusicCatalogProvider provider)
    {
        _provider = provider
            ?? throw new ArgumentNullException(nameof(provider));

        if (string.IsNullOrWhiteSpace(_provider.ProviderId))
        {
            throw new ArgumentException(
                "Music provider must have a valid identifier.",
                nameof(provider));
        }
    }

    public async Task<IReadOnlyList<MusicTrack>> SearchTracksAsync(
        string query,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw ValidationError(
                "Search query is required.");
        }

        var normalizedQuery = query.Trim();

        if (normalizedQuery.Length is
            < MinQueryLength or > MaxQueryLength)
        {
            throw ValidationError(
                "Search query length must be between 2 and 100 characters.");
        }

        if (limit is < MinSearchLimit or > MaxSearchLimit)
        {
            throw ValidationError(
                "Search limit must be between 1 and 50.");
        }

        try
        {
            var tracks = await _provider.SearchAsync(
                normalizedQuery,
                limit,
                cancellationToken);

            if (tracks is null)
            {
                throw InvalidProviderResponse();
            }

            foreach (var track in tracks)
            {
                if (track is null ||
                    track.Reference is null ||
                    !string.Equals(
                        track.Reference.Provider,
                        _provider.ProviderId,
                        StringComparison.Ordinal))
                {
                    throw InvalidProviderResponse();
                }
            }

            return Array.AsReadOnly(tracks.ToArray());
        }
        catch (MusicProviderException exception)
        {
            throw MapProviderException(exception);
        }
    }

    public async Task<MusicTrack?> GetTrackAsync(
        TrackReference reference,
        CancellationToken cancellationToken = default)
    {
        if (reference is null ||
            string.IsNullOrWhiteSpace(reference.Provider) ||
            string.IsNullOrWhiteSpace(reference.TrackId))
        {
            throw ValidationError(
                "A valid track reference is required.");
        }

        if (!string.Equals(
            reference.Provider,
            _provider.ProviderId,
            StringComparison.Ordinal))
        {
            throw new MusicCatalogApplicationException(
                ApplicationErrorCode.UnsupportedMusicProvider,
                "The specified music provider is not supported.");
        }

        try
        {
            var track = await _provider.GetByIdAsync(
                reference.TrackId,
                cancellationToken);

            if (track is null)
            {
                return null;
            }

            if (track.Reference != reference)
            {
                throw InvalidProviderResponse();
            }

            return track;
        }
        catch (MusicProviderException exception)
        {
            throw MapProviderException(exception);
        }
    }

    private static MusicCatalogApplicationException ValidationError(
        string message)
    {
        return new MusicCatalogApplicationException(
            ApplicationErrorCode.Validation,
            message);
    }

    private static MusicProviderException InvalidProviderResponse()
    {
        return new MusicProviderException(
            MusicProviderFailureKind.InvalidResponse,
            "Music provider returned inconsistent track data.");
    }

    private static MusicCatalogApplicationException MapProviderException(
        MusicProviderException exception)
    {
        var errorCode = exception.FailureKind ==
            MusicProviderFailureKind.RateLimited
                ? ApplicationErrorCode.MusicProviderRateLimited
                : ApplicationErrorCode.MusicProviderUnavailable;

        var message = errorCode ==
            ApplicationErrorCode.MusicProviderRateLimited
                ? "Music provider rate limit exceeded."
                : "Music provider is currently unavailable.";

        return new MusicCatalogApplicationException(
            errorCode,
            message,
            exception);
    }
}