using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using VibyRoom.Application.Abstractions.Music;

namespace VibyRoom.Infrastructure.Ai;

public sealed class GigaChatClient : IAiMusicProvider
{
    private const string CompletionUrl =
        "https://api.giga.chat/v1/chat/completions";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GigaChatAccessTokenProvider _tokenProvider;

    public GigaChatClient(
        IHttpClientFactory httpClientFactory,
        GigaChatAccessTokenProvider tokenProvider,
        IOptions<GigaChatOptions> options)
    {
        _httpClientFactory = httpClientFactory
            ?? throw new ArgumentNullException(nameof(httpClientFactory));

        _tokenProvider = tokenProvider
            ?? throw new ArgumentNullException(nameof(tokenProvider));

        ArgumentNullException.ThrowIfNull(options);
    }

    public async Task<string> GenerateAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);

        var payload = JsonSerializer.Serialize(
            new
            {
                model = "GigaChat-2",
                messages = new[]
                {
                    new
                    {
                        role = "system",
                        content = systemPrompt
                    },
                    new
                    {
                        role = "user",
                        content = userPrompt
                    }
                },
                stream = false,
                max_tokens = 2048
            });

        var client = _httpClientFactory.CreateClient(
            GigaChatOptions.ChatClientName);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var accessToken = await _tokenProvider
                .GetAccessTokenAsync(cancellationToken);

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                CompletionUrl);

            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    accessToken);

            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue(
                    "application/json"));

            request.Content = new StringContent(
                payload,
                Encoding.UTF8,
                "application/json");

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized &&
                attempt == 0)
            {
                await _tokenProvider.InvalidateAsync(
                    accessToken,
                    cancellationToken);

                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"GigaChat completion returned HTTP {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken);

            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty(
                    "choices",
                    out var choices) ||
                choices.ValueKind != JsonValueKind.Array ||
                choices.GetArrayLength() == 0)
            {
                throw new InvalidDataException(
                    "GigaChat returned no completion choices.");
            }

            var choice = choices[0];

            if (!choice.TryGetProperty(
                    "message",
                    out var message) ||
                !message.TryGetProperty(
                    "content",
                    out var content) ||
                content.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException(
                    "GigaChat returned an invalid completion.");
            }

            var result = content.GetString();

            if (string.IsNullOrWhiteSpace(result))
            {
                throw new InvalidDataException(
                    "GigaChat returned empty content.");
            }

            return result;
        }

        throw new InvalidOperationException(
            "GigaChat authorization retry failed.");
    }
}