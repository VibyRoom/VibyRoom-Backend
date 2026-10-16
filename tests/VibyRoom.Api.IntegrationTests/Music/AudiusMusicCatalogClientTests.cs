using System.Net;
using Microsoft.Extensions.Options;
using VibyRoom.Infrastructure.Music;
using VibyRoom.Infrastructure.Music.Configuration;
using Xunit;

namespace VibyRoom.Api.IntegrationTests.Music;

public sealed class AudiusMusicCatalogClientTests
{
    [Fact]
    public async Task SearchAsync_EncodesQueryAndMapsTrack()
    {
        using var handler = new StubHttpMessageHandler(
            (request, _) =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);

                Assert.Equal(
                    "https://api.audius.co/v1/tracks/search" +
                    "?query=C%23%20%26%20synth&limit=2",
                    request.RequestUri?.AbsoluteUri);

                Assert.Equal(
                    "application/json",
                    Assert.Single(request.Headers.Accept).MediaType);

                Assert.Null(request.Headers.Authorization);

                return Task.FromResult(
                    StubHttpMessageHandler.Json(
                        HttpStatusCode.OK,
                        """
                        {
                          "data": [
                            {
                              "id": "D7KyD",
                              "title": "Example Track",
                              "duration": 183,
                              "user": {
                                "id": "artist123",
                                "name": "Example Artist",
                                "handle": "exampleartist"
                              },
                              "artwork": {
                                "150x150": "https://images.example.com/small.jpg",
                                "480x480": "https://images.example.com/medium.jpg"
                              },
                              "permalink": "https://audius.co/exampleartist/example-track"
                            }
                          ]
                        }
                        """));
            });

        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var tracks = await client.SearchAsync(
            "C# & synth",
            2,
            TestContext.Current.CancellationToken);

        var track = Assert.Single(tracks);

        Assert.Equal("audius", track.Reference.Provider);
        Assert.Equal("D7KyD", track.Reference.TrackId);
        Assert.Equal("Example Track", track.Title);
        Assert.Equal("Example Artist", Assert.Single(track.Artists));
        Assert.Null(track.Album);
        Assert.Equal(183000L, track.DurationMs);

        Assert.Equal(
            "https://images.example.com/medium.jpg",
            track.ArtworkUrl);

        Assert.Equal(
            "https://audius.co/exampleartist/example-track",
            track.ExternalUrl);

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task SearchAsync_NoMatches_ReturnsEmptyCollection()
    {
        using var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(
                StubHttpMessageHandler.Json(
                    HttpStatusCode.OK,
                    """{"data":[]}""")));

        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var tracks = await client.SearchAsync(
            "unknown composition",
            20,
            TestContext.Current.CancellationToken);

        Assert.Empty(tracks);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task SearchAsync_MissingOptionalFields_UsesNull()
    {
        using var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(
                StubHttpMessageHandler.Json(
                    HttpStatusCode.OK,
                    """
                    {
                      "data": [
                        {
                          "id": "D7KyD",
                          "title": "Minimal Track"
                        }
                      ]
                    }
                    """)));

        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var tracks = await client.SearchAsync(
            "minimal",
            20,
            TestContext.Current.CancellationToken);

        var track = Assert.Single(tracks);

        Assert.Equal("Minimal Track", track.Title);
        Assert.Empty(track.Artists);
        Assert.Null(track.Album);
        Assert.Null(track.DurationMs);
        Assert.Null(track.ArtworkUrl);
        Assert.Null(track.ExternalUrl);
    }

    [Fact]
    public async Task SearchAsync_InvalidArtworkUrl_UsesFallback()
    {
        using var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(
                StubHttpMessageHandler.Json(
                    HttpStatusCode.OK,
                    """
                    {
                      "data": [
                        {
                          "id": "D7KyD",
                          "title": "Artwork Track",
                          "artwork": {
                            "480x480": "http://insecure.example.com/image.jpg",
                            "1000x1000": "https://images.example.com/large.jpg",
                            "150x150": "https://images.example.com/small.jpg"
                          },
                          "permalink": "javascript:alert(1)"
                        }
                      ]
                    }
                    """)));

        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var tracks = await client.SearchAsync(
            "artwork",
            20,
            TestContext.Current.CancellationToken);

        var track = Assert.Single(tracks);

        Assert.Equal(
            "https://images.example.com/large.jpg",
            track.ArtworkUrl);

        Assert.Null(track.ExternalUrl);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsTrack()
    {
        using var handler = new StubHttpMessageHandler(
            (request, _) =>
            {
                Assert.Equal(
                    "https://api.audius.co/v1/tracks/D7KyD",
                    request.RequestUri?.AbsoluteUri);

                return Task.FromResult(
                    StubHttpMessageHandler.Json(
                        HttpStatusCode.OK,
                        """
                        {
                          "data": {
                            "id": "D7KyD",
                            "title": "Details Track",
                            "duration": 210,
                            "user": {
                              "name": "Details Artist"
                            },
                            "permalink": "https://audius.co/artist/details"
                          }
                        }
                        """));
            });

        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var track = await client.GetByIdAsync(
            "D7KyD",
            TestContext.Current.CancellationToken);

        Assert.NotNull(track);
        Assert.Equal("D7KyD", track.Reference.TrackId);
        Assert.Equal("Details Track", track.Title);
        Assert.Equal("Details Artist", Assert.Single(track.Artists));
        Assert.Equal(210000L, track.DurationMs);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsNull()
    {
        using var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound)));

        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var track = await client.GetByIdAsync(
            "D7KyD",
            TestContext.Current.CancellationToken);

        Assert.Null(track);
        Assert.Equal(1, handler.RequestCount);
    }

    [Theory]
    [InlineData("../invalid")]
    [InlineData("invalid id")]
    [InlineData("invalid_id")]
    [InlineData("")]
    public async Task GetByIdAsync_InvalidId_SkipsHttpRequest(
        string trackId)
    {
        using var handler = new StubHttpMessageHandler(
            (_, _) => throw new InvalidOperationException(
                "HTTP request should not be performed."));

        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var track = await client.GetByIdAsync(
            trackId,
            TestContext.Current.CancellationToken);

        Assert.Null(track);
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task SearchAsync_SendsConfiguredBearerToken()
    {
        const string token = "test-token-not-a-real-secret";

        using var handler = new StubHttpMessageHandler(
            (request, _) =>
            {
                Assert.Equal(
                    "Bearer",
                    request.Headers.Authorization?.Scheme);

                Assert.Equal(
                    token,
                    request.Headers.Authorization?.Parameter);

                return Task.FromResult(
                    StubHttpMessageHandler.Json(
                        HttpStatusCode.OK,
                        """{"data":[]}"""));
            });

        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient, token);

        await client.SearchAsync(
            "electronic",
            20,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task SearchAsync_InvalidLimit_SkipsHttpRequest()
    {
        using var handler = new StubHttpMessageHandler(
            (_, _) => throw new InvalidOperationException(
                "HTTP request should not be performed."));

        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => client.SearchAsync(
                "electronic",
                51,
                TestContext.Current.CancellationToken));

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task SearchAsync_NullDurationAndArtist_RemainUnknown()
    {
        using var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(
                StubHttpMessageHandler.Json(
                    HttpStatusCode.OK,
                    """
                    {
                      "data": [
                        {
                          "id": "D7KyD",
                          "title": "Unknown Metadata",
                          "duration": null,
                          "user": null,
                          "artwork": null,
                          "permalink": null
                        }
                      ]
                    }
                    """)));

        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        var tracks = await client.SearchAsync(
            "unknown metadata",
            20,
            TestContext.Current.CancellationToken);

        var track = Assert.Single(tracks);

        Assert.Empty(track.Artists);
        Assert.Null(track.DurationMs);
        Assert.Null(track.ArtworkUrl);
        Assert.Null(track.ExternalUrl);
    }

    [Fact]
    public void Constructor_ConfiguresHttpTimeout()
    {
        using var handler = new StubHttpMessageHandler(
            (_, _) => Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)));

        using var httpClient = new HttpClient(handler);

        _ = CreateClient(
            httpClient,
            timeoutSeconds: 8);

        Assert.Equal(
            TimeSpan.FromSeconds(8),
            httpClient.Timeout);
    }

    private static AudiusMusicCatalogClient CreateClient(
        HttpClient httpClient,
        string? bearerToken = null,
        int timeoutSeconds = 15)
    {
        return new AudiusMusicCatalogClient(
            httpClient,
            Options.Create(new AudiusProviderOptions
            {
                Provider = "audius",
                BaseUrl = "https://api.audius.co/v1/",
                BearerToken = bearerToken,
                TimeoutSeconds = timeoutSeconds
            }));
    }
}