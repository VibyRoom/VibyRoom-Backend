using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VibyRoom.Application.Abstractions.Exceptions;
using VibyRoom.Application.Abstractions.Music;
using VibyRoom.Application.Contracts.Music;
using VibyRoom.Domain.Music.Tracks;
using VibyRoom.Infrastructure.Music.Configuration;

namespace VibyRoom.Infrastructure.Music;

public sealed class AudiusStreamProvider : IMusicStreamProvider
{
    private const int MaxRedirects = 5;
    private const int ProbeBytes = 8;

    private readonly HttpClient _httpClient;
    private readonly ILogger<AudiusStreamProvider> _logger;
    private readonly HashSet<string> _additionalHosts;

    public AudiusStreamProvider(
        HttpClient httpClient,
        IOptions<AudiusProviderOptions> options,
        ILogger<AudiusStreamProvider> logger)
    {
        _httpClient = httpClient
            ?? throw new ArgumentNullException(nameof(httpClient));

        _logger = logger
            ?? throw new ArgumentNullException(nameof(logger));

        ArgumentNullException.ThrowIfNull(options);

        var settings = options.Value;

        if (!string.Equals(
                settings.Provider,
                AudiusProviderOptions.ProviderId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Unsupported music provider.",
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
            baseUri.AbsolutePath != "/v1/" ||
            !baseUri.IsDefaultPort ||
            !string.IsNullOrEmpty(baseUri.UserInfo) ||
            !string.IsNullOrEmpty(baseUri.Query) ||
            !string.IsNullOrEmpty(baseUri.Fragment))
        {
            throw new ArgumentException(
                "Invalid Audius API base URL.",
                nameof(options));
        }

        if (settings.TimeoutSeconds is < 1 or > 120)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Audius timeout must be between 1 and 120 seconds.");
        }

        _additionalHosts = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var configuredHost in
                 settings.AllowedStreamRedirectHosts ?? [])
        {
            if (string.IsNullOrWhiteSpace(configuredHost))
            {
                throw new ArgumentException(
                    "Audius redirect host cannot be empty.",
                    nameof(options));
            }

            var host = configuredHost.Trim();

            if (!IsValidConfiguredHost(host))
            {
                throw new ArgumentException(
                    $"Invalid Audius redirect host: {host}.",
                    nameof(options));
            }

            _additionalHosts.Add(host);
        }

        _httpClient.BaseAddress = baseUri;
        _httpClient.Timeout =
            TimeSpan.FromSeconds(settings.TimeoutSeconds);
    }

    public async Task<bool> CanStreamAsync(
        TrackReference reference,
        CancellationToken cancellationToken = default)
    {
        return await GetStreamSourceAsync(
            reference,
            cancellationToken) is not null;
    }

    public async Task<PlaybackSource?> GetStreamSourceAsync(
        TrackReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (!string.Equals(
                reference.Provider,
                AudiusProviderOptions.ProviderId,
                StringComparison.Ordinal) ||
            !IsValidTrackId(reference.TrackId))
        {
            return null;
        }

        var streamUrl = new Uri(
            _httpClient.BaseAddress!,
            $"tracks/{Uri.EscapeDataString(reference.TrackId)}/stream");

        try
        {
            if (!await IsPublicTrackAsync(
                    reference.TrackId,
                    cancellationToken))
            {
                return null;
            }

            return await ProbeAsync(
                reference,
                streamUrl,
                cancellationToken);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new MusicProviderException(
                MusicProviderFailureKind.Unavailable,
                "Audius stream request timed out.",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new MusicProviderException(
                MusicProviderFailureKind.Unavailable,
                "Audius stream request failed.",
                exception);
        }
        catch (IOException exception)
        {
            throw new MusicProviderException(
                MusicProviderFailureKind.Unavailable,
                "Audius audio stream could not be read.",
                exception);
        }
    }

    private async Task<bool> IsPublicTrackAsync(
        string trackId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"tracks/{Uri.EscapeDataString(trackId)}");

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode is
            HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return false;
        }

        if (response.StatusCode is
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new MusicProviderException(
                MusicProviderFailureKind.AccessDenied,
                "Audius denied access to track metadata.");
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new MusicProviderException(
                MusicProviderFailureKind.RateLimited,
                "Audius rate limit exceeded.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new MusicProviderException(
                MusicProviderFailureKind.Unavailable,
                "Audius track metadata is unavailable.");
        }

        try
        {
            await using var stream =
                await response.Content.ReadAsStreamAsync(
                    cancellationToken);

            using var document = await JsonDocument.ParseAsync(
                stream,
                new JsonDocumentOptions { MaxDepth = 32 },
                cancellationToken);

            if (!document.RootElement.TryGetProperty(
                    "data",
                    out var track) ||
                track.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException(
                    "Missing Audius track data.");
            }

            if (!ReadBoolean(track, "is_streamable"))
            {
                return false;
            }

            if (ReadBoolean(track, "is_stream_gated") ||
                ReadBoolean(track, "is_unlisted"))
            {
                return false;
            }

            if (track.TryGetProperty(
                    "stream_conditions",
                    out var conditions) &&
                conditions.ValueKind is not (
                    JsonValueKind.Null or JsonValueKind.Undefined))
            {
                return false;
            }

            return true;
        }
        catch (JsonException exception)
        {
            throw new MusicProviderException(
                MusicProviderFailureKind.InvalidResponse,
                "Audius returned invalid track metadata.",
                exception);
        }
    }

    private async Task<PlaybackSource?> ProbeAsync(
        TrackReference reference,
        Uri publicStreamUrl,
        CancellationToken cancellationToken)
    {
        var requestUri = publicStreamUrl;

        for (var redirectCount = 0;
             redirectCount <= MaxRedirects;
             redirectCount++)
        {
            EnsureAllowedUri(requestUri);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                requestUri);

            request.Headers.Range =
                new RangeHeaderValue(0, ProbeBytes - 1);

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (IsRedirect(response.StatusCode))
            {
                var location = response.Headers.Location;

                if (location is null)
                {
                    throw InvalidResponse(
                        "Audius redirect has no location.");
                }

                requestUri = location.IsAbsoluteUri
                    ? location
                    : new Uri(requestUri, location);

                continue;
            }

            if (response.StatusCode is
                HttpStatusCode.NotFound or
                HttpStatusCode.Gone or
                HttpStatusCode.BadRequest)
            {
                return null;
            }

            if (response.StatusCode is
                HttpStatusCode.Unauthorized or
                HttpStatusCode.Forbidden)
            {
                throw new MusicProviderException(
                    MusicProviderFailureKind.AccessDenied,
                    "Audius denied public streaming access.");
            }

            if (response.StatusCode ==
                HttpStatusCode.TooManyRequests)
            {
                throw new MusicProviderException(
                    MusicProviderFailureKind.RateLimited,
                    "Audius streaming rate limit exceeded.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new MusicProviderException(
                    MusicProviderFailureKind.Unavailable,
                    "Audius streaming endpoint is unavailable.");
            }

            var contentType =
                response.Content.Headers.ContentType?.MediaType;

            if (!IsAllowedContentType(contentType))
            {
                return null;
            }

            var buffer = new byte[ProbeBytes];
            var readCount = 0;

            await using var stream =
                await response.Content.ReadAsStreamAsync(
                    cancellationToken);

            while (readCount < buffer.Length)
            {
                var count = await stream.ReadAsync(
                    buffer.AsMemory(
                        readCount,
                        buffer.Length - readCount),
                    cancellationToken);

                if (count == 0)
                {
                    break;
                }

                readCount += count;
            }

            if (!LooksLikeMp3(buffer, readCount))
            {
                return null;
            }

            if (response.StatusCode ==
                HttpStatusCode.PartialContent)
            {
                if (response.Content.Headers.ContentRange is
                    not { From: 0 })
                {
                    throw InvalidResponse(
                        "Audius returned an invalid Content-Range.");
                }
            }
            else
            {
                _logger.LogWarning(
                    "Audius stream for track {TrackId} " +
                    "did not honor the Range request.",
                    reference.TrackId);
            }

            return new PlaybackSource(
                reference,
                publicStreamUrl,
                expiresAtUtc: null,
                contentType: contentType is not null &&
                             contentType.StartsWith(
                                 "audio/",
                                 StringComparison.OrdinalIgnoreCase)
                    ? contentType
                    : "audio/mpeg");
        }

        throw InvalidResponse(
            "Audius exceeded the permitted redirect limit.");
    }

    private void EnsureAllowedUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw InvalidResponse(
                "Audius returned an invalid stream URL.");
        }

        var host = uri.IdnHost;

        var allowed =
            IsOfficialAudiusHost(host) ||
            _additionalHosts.Contains(host);

        if (allowed)
        {
            return;
        }

        _logger.LogWarning(
            "Rejected Audius stream redirect host {Host}.",
            host);

        throw InvalidResponse(
            "Audius returned an untrusted stream destination.");
    }

    private static bool IsOfficialAudiusHost(string host)
    {
        return host.Equals(
                   "api.audius.co",
                   StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(
                   ".audius.co",
                   StringComparison.OrdinalIgnoreCase) ||
               host.Equals(
                   "audius.org",
                   StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(
                   ".audius.org",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidConfiguredHost(string host)
    {
        return Uri.CheckHostName(host) == UriHostNameType.Dns &&
               host.Contains('.') &&
               !host.EndsWith(
                   ".local",
                   StringComparison.OrdinalIgnoreCase) &&
               !host.EndsWith(
                   ".internal",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        return statusCode is
            HttpStatusCode.MovedPermanently or
            HttpStatusCode.Redirect or
            HttpStatusCode.RedirectMethod or
            HttpStatusCode.TemporaryRedirect or
            HttpStatusCode.PermanentRedirect;
    }

    private static bool IsAllowedContentType(
        string? contentType)
    {
        return contentType is null ||
               contentType.StartsWith(
                   "audio/",
                   StringComparison.OrdinalIgnoreCase) ||
               contentType.Equals(
                   "application/octet-stream",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeMp3(
        byte[] buffer,
        int length)
    {
        if (length < 3)
        {
            return false;
        }

        if (buffer[0] == (byte)'I' &&
            buffer[1] == (byte)'D' &&
            buffer[2] == (byte)'3')
        {
            return true;
        }

        return buffer[0] == 0xFF &&
               (buffer[1] & 0xE0) == 0xE0;
    }

    private static bool IsValidTrackId(string trackId)
    {
        return !string.IsNullOrEmpty(trackId) &&
               trackId.Length <= 128 &&
               trackId.All(char.IsAsciiLetterOrDigit);
    }

    private static bool ReadBoolean(
        JsonElement element,
        string propertyName)
    {
        return element.TryGetProperty(
                   propertyName,
                   out var property) &&
               property.ValueKind == JsonValueKind.True;
    }

    private static MusicProviderException InvalidResponse(
        string message)
    {
        return new MusicProviderException(
            MusicProviderFailureKind.InvalidResponse,
            message);
    }
}