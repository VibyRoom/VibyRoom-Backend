using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace VibyRoom.Infrastructure.Ai;

public sealed class GigaChatAccessTokenProvider
{
    private const string OAuthUrl =
        "https://ngw.devices.sberbank.ru:9443/api/v2/oauth";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GigaChatOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAtUtc;

    public GigaChatAccessTokenProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<GigaChatOptions> options,
        TimeProvider timeProvider)
    {
        _httpClientFactory = httpClientFactory
            ?? throw new ArgumentNullException(nameof(httpClientFactory));

        _options = options?.Value
            ?? throw new ArgumentNullException(nameof(options));

        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<string> GetAccessTokenAsync(
        CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);

        try
        {
            if (_cachedToken is not null &&
                _expiresAtUtc >
                _timeProvider.GetUtcNow().AddMinutes(2))
            {
                return _cachedToken;
            }

            return await RefreshAsync(cancellationToken);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task InvalidateAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);

        try
        {
            if (string.Equals(
                    _cachedToken,
                    accessToken,
                    StringComparison.Ordinal))
            {
                _cachedToken = null;
                _expiresAtUtc = default;
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<string> RefreshAsync(
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.AuthorizationKey))
        {
            throw new InvalidOperationException(
                "GigaChat authorization key is not configured.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            OAuthUrl);

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Basic",
                _options.AuthorizationKey.Trim());

        request.Headers.TryAddWithoutValidation(
            "RqUID",
            Guid.NewGuid().ToString("D"));

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));

        request.Content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["scope"] = "GIGACHAT_API_PERS"
            });

        var client = _httpClientFactory.CreateClient(
            GigaChatOptions.OAuthClientName);

        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"GigaChat OAuth returned HTTP {(int)response.StatusCode}.");
        }

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken);

        using var document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);

        var root = document.RootElement;

        if (!root.TryGetProperty(
                "access_token",
                out var tokenElement) ||
            !root.TryGetProperty(
                "expires_at",
                out var expirationElement))
        {
            throw new InvalidDataException(
                "GigaChat OAuth response is incomplete.");
        }

        var accessToken = tokenElement.GetString();
        var expiration = expirationElement.GetInt64();

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            throw new InvalidDataException(
                "GigaChat returned an empty access token.");
        }

        var expiresAtUtc = expiration > 10_000_000_000
            ? DateTimeOffset.FromUnixTimeMilliseconds(expiration)
            : DateTimeOffset.FromUnixTimeSeconds(expiration);

        if (expiresAtUtc <= _timeProvider.GetUtcNow())
        {
            throw new InvalidDataException(
                "GigaChat returned an expired access token.");
        }

        _cachedToken = accessToken;
        _expiresAtUtc = expiresAtUtc;

        return accessToken;
    }
}