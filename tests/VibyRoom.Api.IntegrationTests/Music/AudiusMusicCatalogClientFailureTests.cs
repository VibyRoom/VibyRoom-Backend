using System.Net;
using Microsoft.Extensions.Options;
using VibyRoom.Application.Abstractions.Exceptions;
using VibyRoom.Infrastructure.Music;
using VibyRoom.Infrastructure.Music.Configuration;
using Xunit;

namespace VibyRoom.Api.IntegrationTests.Music;

public sealed class AudiusMusicCatalogClientFailureTests
{
    [Theory]
    [InlineData(
        HttpStatusCode.Unauthorized,
        MusicProviderFailureKind.AccessDenied)]
    [InlineData(
        HttpStatusCode.Forbidden,
        MusicProviderFailureKind.AccessDenied)]
    [InlineData(
        HttpStatusCode.TooManyRequests,
        MusicProviderFailureKind.RateLimited)]
    [InlineData(
        HttpStatusCode.BadRequest,
        MusicProviderFailureKind.Unavailable)]
    [InlineData(
        HttpStatusCode.NotFound,
        MusicProviderFailureKind.Unavailable)]
    [InlineData(
        HttpStatusCode.InternalServerError,
        MusicProviderFailureKind.Unavailable)]
    [InlineData(
        HttpStatusCode.BadGateway,
        MusicProviderFailureKind.Unavailable)]
    [InlineData(
        HttpStatusCode.ServiceUnavailable,
        MusicProviderFailureKind.Unavailable)]
    public async Task SearchAsync_HttpFailure_MapsError(
        HttpStatusCode statusCode,
        MusicProviderFailureKind expectedKind)
    {
        using var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(
                new HttpResponseMessage(statusCode)));

        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var exception =
            await Assert.ThrowsAsync<MusicProviderException>(
                () => client.SearchAsync(
                    "electronic",
                    20,
                    TestContext.Current.CancellationToken));

        Assert.Equal(expectedKind, exception.FailureKind);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task SearchAsync_InvalidJson_ThrowsInvalidResponse()
    {
        using var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(
                StubHttpMessageHandler.Json(
                    HttpStatusCode.OK,
                    "{ invalid json }")));

        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var exception =
            await Assert.ThrowsAsync<MusicProviderException>(
                () => client.SearchAsync(
                    "electronic",
                    20,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MusicProviderFailureKind.InvalidResponse,
            exception.FailureKind);
    }

    [Fact]
    public async Task SearchAsync_MissingData_ThrowsInvalidResponse()
    {
        using var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(
                StubHttpMessageHandler.Json(
                    HttpStatusCode.OK,
                    """{"message":"ok"}""")));

        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var exception =
            await Assert.ThrowsAsync<MusicProviderException>(
                () => client.SearchAsync(
                    "electronic",
                    20,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MusicProviderFailureKind.InvalidResponse,
            exception.FailureKind);
    }

    [Theory]
    [InlineData(
        """{"data":[{"id":"D7KyD"}]}""")]
    [InlineData(
        """{"data":[{"title":"Missing ID"}]}""")]
    [InlineData(
        """{"data":[null]}""")]
    [InlineData(
        """{"data":null}""")]
    [InlineData(
        """{"data":{"id":"D7KyD"}}""")]
    public async Task SearchAsync_InvalidPayload_ThrowsInvalidResponse(
        string json)
    {
        using var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(
                StubHttpMessageHandler.Json(
                    HttpStatusCode.OK,
                    json)));

        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var exception =
            await Assert.ThrowsAsync<MusicProviderException>(
                () => client.SearchAsync(
                    "electronic",
                    20,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MusicProviderFailureKind.InvalidResponse,
            exception.FailureKind);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    public async Task GetByIdAsync_InvalidDuration_ThrowsInvalidResponse(
        long duration)
    {
        using var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(
                StubHttpMessageHandler.Json(
                    HttpStatusCode.OK,
                    $$"""
                    {
                      "data": {
                        "id": "D7KyD",
                        "title": "Invalid Duration",
                        "duration": {{duration}}
                      }
                    }
                    """)));

        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var exception =
            await Assert.ThrowsAsync<MusicProviderException>(
                () => client.GetByIdAsync(
                    "D7KyD",
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MusicProviderFailureKind.InvalidResponse,
            exception.FailureKind);
    }

    [Fact]
    public async Task SearchAsync_NetworkFailure_ThrowsUnavailable()
    {
        using var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromException<HttpResponseMessage>(
                new HttpRequestException(
                    "Simulated connection failure.")));

        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var exception =
            await Assert.ThrowsAsync<MusicProviderException>(
                () => client.SearchAsync(
                    "electronic",
                    20,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MusicProviderFailureKind.Unavailable,
            exception.FailureKind);
    }

    [Fact]
    public async Task SearchAsync_Timeout_ThrowsUnavailable()
    {
        using var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromException<HttpResponseMessage>(
                new TaskCanceledException(
                    "Simulated HTTP timeout.")));

        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var exception =
            await Assert.ThrowsAsync<MusicProviderException>(
                () => client.SearchAsync(
                    "electronic",
                    20,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            MusicProviderFailureKind.Unavailable,
            exception.FailureKind);
    }

    [Fact]
    public async Task SearchAsync_CallerCancellation_IsPropagated()
    {
        using var source = new CancellationTokenSource();

        using var handler = new StubHttpMessageHandler(
            async (_, cancellationToken) =>
            {
                Assert.True(cancellationToken.CanBeCanceled);

                source.Cancel();

                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    cancellationToken);

                return new HttpResponseMessage(
                    HttpStatusCode.OK);
            });

        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SearchAsync(
                "electronic",
                20,
                source.Token));

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task SearchAsync_ErrorBody_IsNotExposed()
    {
        const string secret = "sensitive-provider-response";

        using var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(
                StubHttpMessageHandler.Json(
                    HttpStatusCode.ServiceUnavailable,
                    $$"""
                    {
                      "error": "{{secret}}"
                    }
                    """)));

        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var exception =
            await Assert.ThrowsAsync<MusicProviderException>(
                () => client.SearchAsync(
                    "electronic",
                    20,
                    TestContext.Current.CancellationToken));

        Assert.DoesNotContain(
            secret,
            exception.Message);

        Assert.Equal(
            MusicProviderFailureKind.Unavailable,
            exception.FailureKind);
    }

    [Fact]
    public void Constructor_InvalidProvider_IsRejected()
    {
        using var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)));

        using var httpClient = new HttpClient(handler);

        Assert.Throws<ArgumentException>(
            () => new AudiusMusicCatalogClient(
                httpClient,
                Options.Create(new AudiusProviderOptions
                {
                    Provider = "spotify",
                    BaseUrl = "https://api.audius.co/v1/"
                })));
    }

    [Fact]
    public void Constructor_UntrustedBaseUrl_IsRejected()
    {
        using var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)));

        using var httpClient = new HttpClient(handler);

        Assert.Throws<ArgumentException>(
            () => new AudiusMusicCatalogClient(
                httpClient,
                Options.Create(new AudiusProviderOptions
                {
                    Provider = "audius",
                    BaseUrl = "https://example.com/v1/"
                })));
    }

    private static AudiusMusicCatalogClient CreateClient(
        HttpClient httpClient)
    {
        return new AudiusMusicCatalogClient(
            httpClient,
            Options.Create(new AudiusProviderOptions
            {
                Provider = "audius",
                BaseUrl = "https://api.audius.co/v1/",
                TimeoutSeconds = 15
            }));
    }
}