using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VibyRoom.Application.Abstractions.Exceptions;
using VibyRoom.Application.Abstractions.Music;
using VibyRoom.Application.Contracts.Music;
using VibyRoom.Domain.Music.Tracks;
using VibyRoom.Infrastructure.Music.Configuration;
using VibyRoom.Infrastructure.Music.Dto;

namespace VibyRoom.Infrastructure.Music;

public sealed class AudiusMusicCatalogClient : IMusicCatalogProvider
{
    private readonly HttpClient _httpClient;
    private readonly string? _bearerToken;

    public string ProviderId => AudiusProviderOptions.ProviderId;

    public AudiusMusicCatalogClient(
        HttpClient httpClient,
        IOptions<AudiusProviderOptions> options)
    {
        _httpClient = httpClient
            ?? throw new ArgumentNullException(nameof(httpClient));

        ArgumentNullException.ThrowIfNull(options);

        var settings = options.Value;

        if (!string.Equals(
                settings.Provider,
                ProviderId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Invalid music provider configuration.",
                nameof(options));
        }

        if (!Uri.TryCreate(
                settings.BaseUrl,
                UriKind.Absolute,
                out var baseUri) ||
            baseUri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(
                baseUri.Host,
                "api.audius.co",
                StringComparison.OrdinalIgnoreCase) ||
            baseUri.AbsolutePath != "/v1/")
        {
            throw new ArgumentException(
                "Audius BaseUrl must be https://api.audius.co/v1/.",
                nameof(options));
        }

        if (settings.TimeoutSeconds is < 1 or > 120)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Audius timeout must be between 1 and 120 seconds.");
        }

        _httpClient.BaseAddress = baseUri;
        _httpClient.Timeout =
            TimeSpan.FromSeconds(settings.TimeoutSeconds);

        _bearerToken = string.IsNullOrWhiteSpace(settings.BearerToken)
            ? null
            : settings.BearerToken.Trim();
    }

    public async Task<IReadOnlyList<MusicTrack>> SearchAsync(
        string query,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        if (limit is < 1 or > 50)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                "Search limit must be between 1 and 50.");
        }

        var requestUri =
            $"tracks/search?query={Uri.EscapeDataString(query)}" +
            $"&limit={limit.ToString(CultureInfo.InvariantCulture)}";

        var tracks = await GetAsync<List<AudiusTrackDto>>(
            requestUri,
            allowNotFound: false,
            cancellationToken);

        if (tracks is null)
        {
            throw InvalidResponse();
        }

        var result = new List<MusicTrack>(tracks.Count);

        foreach (var track in tracks)
        {
            if (track is null)
            {
                throw InvalidResponse();
            }

            result.Add(MapTrack(track));
        }

        return result.AsReadOnly();
    }

    public async Task<MusicTrack?> GetByIdAsync(
        string trackId,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidTrackId(trackId))
        {
            return null;
        }

        var requestUri =
            $"tracks/{Uri.EscapeDataString(trackId)}";

        var track = await GetAsync<AudiusTrackDto>(
            requestUri,
            allowNotFound: true,
            cancellationToken);

        return track is null ? null : MapTrack(track);
    }

    private async Task<T?> GetAsync<T>(
        string requestUri,
        bool allowNotFound,
        CancellationToken cancellationToken)
        where T : class
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            requestUri);

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));

        if (_bearerToken is not null)
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    _bearerToken);
        }

        try
        {
            using var response = await _httpClient.SendAsync(
                request,
                cancellationToken);

            if (allowNotFound &&
                response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw MapHttpError(response.StatusCode);
            }

            return await ReadResponseAsync<T>(
                response,
                cancellationToken);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new MusicProviderException(
                MusicProviderFailureKind.Unavailable,
                "Audius request timed out.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new MusicProviderException(
                MusicProviderFailureKind.Unavailable,
                "Audius network request failed.",
                exception);
        }
    }

    private static async Task<T> ReadResponseAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var body = await response.Content
                .ReadFromJsonAsync<AudiusApiResponse<T>>(
                    cancellationToken: cancellationToken);

            if (body?.Data is not { } data)
            {
                throw InvalidResponse();
            }

            return data;
        }
        catch (JsonException exception)
        {
            throw new MusicProviderException(
                MusicProviderFailureKind.InvalidResponse,
                "Audius returned invalid JSON.",
                exception);
        }
        catch (NotSupportedException exception)
        {
            throw new MusicProviderException(
                MusicProviderFailureKind.InvalidResponse,
                "Audius returned an unsupported response format.",
                exception);
        }
    }

    private MusicTrack MapTrack(AudiusTrackDto track)
    {
        if (string.IsNullOrWhiteSpace(track.Id) ||
            !IsValidTrackId(track.Id) ||
            string.IsNullOrWhiteSpace(track.Title))
        {
            throw InvalidResponse();
        }

        long? durationMs = null;

        if (track.DurationSeconds.HasValue)
        {
            var seconds = track.DurationSeconds.Value;

            if (seconds < 0 || seconds > long.MaxValue / 1000)
            {
                throw InvalidResponse();
            }

            if (seconds > 0)
            {
                durationMs = seconds * 1000;
            }
        }

        IReadOnlyList<string> artists =
            string.IsNullOrWhiteSpace(track.User?.Name)
                ? Array.Empty<string>()
                : new[] { track.User.Name };

        var artworkUrl = GetPublicUrl(
            track.Artwork?.MediumUrl);

        artworkUrl ??= GetPublicUrl(
            track.Artwork?.LargeUrl);

        artworkUrl ??= GetPublicUrl(
            track.Artwork?.SmallUrl);

        var externalUrl = GetPublicUrl(
            track.Permalink);

        return new MusicTrack(
            new TrackReference(ProviderId, track.Id),
            track.Title,
            artists,
            album: null,
            durationMs,
            artworkUrl,
            externalUrl);
    }

    private static string? GetPublicUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Uri.TryCreate(
                value,
                UriKind.Absolute,
                out var uri))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }

        return uri.AbsoluteUri;
    }

    private static bool IsValidTrackId(string? trackId)
    {
        if (string.IsNullOrEmpty(trackId) ||
            trackId.Length > 128)
        {
            return false;
        }

        foreach (var character in trackId)
        {
            if (!char.IsAsciiLetterOrDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    private static MusicProviderException MapHttpError(
        HttpStatusCode statusCode)
    {
        var kind = statusCode switch
        {
            HttpStatusCode.TooManyRequests =>
                MusicProviderFailureKind.RateLimited,

            HttpStatusCode.Unauthorized or
            HttpStatusCode.Forbidden =>
                MusicProviderFailureKind.AccessDenied,

            _ => MusicProviderFailureKind.Unavailable
        };

        return new MusicProviderException(
            kind,
            kind switch
            {
                MusicProviderFailureKind.RateLimited =>
                    "Audius rate limit exceeded.",

                MusicProviderFailureKind.AccessDenied =>
                    "Audius rejected provider authentication.",

                _ => "Audius request was unsuccessful."
            });
    }

    private static MusicProviderException InvalidResponse()
    {
        return new MusicProviderException(
            MusicProviderFailureKind.InvalidResponse,
            "Audius returned incomplete or inconsistent track data.");
    }
}