using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VibyRoom.Application.Abstractions.Exceptions;
using VibyRoom.Application.Contracts.Music;
using VibyRoom.Domain.Music.Tracks;
using Xunit;

namespace VibyRoom.Api.IntegrationTests.Music;

public sealed class MusicControllerIntegrationTests
{
    private const string SearchUrl =
        "/api/v1/music/tracks/search";

    private const string DetailsUrl =
        "/api/v1/music/tracks/details";

    [Fact]
    public async Task Search_AuthenticatedGuest_ReturnsTracks()
    {
        using var host = new MusicApiTestHost();

        await host.CreateGuestSessionAsync(
            TestContext.Current.CancellationToken);

        var track = CreateTrack();

        host.Provider.SearchBehavior = (_, _, _) =>
            Task.FromResult<IReadOnlyList<MusicTrack>>(
                new[] { track });

        using var response = await host.Client.GetAsync(
            $"{SearchUrl}?query=%20electronic%20&limit=7",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await ReadJsonAsync(response);

        var tracks = json.GetProperty("tracks");

        Assert.Equal(1, tracks.GetArrayLength());

        var item = tracks[0];

        Assert.Equal(
            "Example Track",
            item.GetProperty("title").GetString());

        Assert.Equal(
            "audius",
            item.GetProperty("reference")
                .GetProperty("provider").GetString());

        Assert.Equal(
            "D7KyD",
            item.GetProperty("reference")
                .GetProperty("trackId").GetString());

        Assert.Equal(
            "Example Artist",
            item.GetProperty("artists")[0].GetString());

        Assert.Equal(
            180000L,
            item.GetProperty("durationMs").GetInt64());

        Assert.Equal(
            "electronic",
            host.Provider.LastSearchQuery);

        Assert.Equal(7, host.Provider.LastSearchLimit);
        Assert.Equal(1, host.Provider.SearchCalls);
    }

    [Fact]
    public async Task Search_DefaultLimit_IsTwenty()
    {
        using var host = new MusicApiTestHost();

        await host.CreateGuestSessionAsync(
            TestContext.Current.CancellationToken);

        using var response = await host.Client.GetAsync(
            $"{SearchUrl}?query=electronic",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(20, host.Provider.LastSearchLimit);
    }

    [Fact]
    public async Task Search_NoMatches_ReturnsEmptyArray()
    {
        using var host = new MusicApiTestHost();

        await host.CreateGuestSessionAsync(
            TestContext.Current.CancellationToken);

        using var response = await host.Client.GetAsync(
            $"{SearchUrl}?query=unknown",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await ReadJsonAsync(response);

        Assert.Equal(
            0,
            json.GetProperty("tracks").GetArrayLength());
    }

    [Theory]
    [InlineData("?query=a")]
    [InlineData("?query=electronic&limit=0")]
    [InlineData("?query=electronic&limit=51")]
    [InlineData("?query=electronic&limit=invalid")]
    [InlineData("?limit=20")]
    public async Task Search_InvalidParameters_Returns400(
        string parameters)
    {
        using var host = new MusicApiTestHost();

        await host.CreateGuestSessionAsync(
            TestContext.Current.CancellationToken);

        using var response = await host.Client.GetAsync(
            SearchUrl + parameters,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);

        var json = await ReadJsonAsync(response);

        Assert.Equal(
            "Validation",
            json.GetProperty("code").GetString());

        Assert.Equal(0, host.Provider.SearchCalls);
    }

    [Fact]
    public async Task Details_ExistingTrack_ReturnsMetadata()
    {
        using var host = new MusicApiTestHost();

        await host.CreateGuestSessionAsync(
            TestContext.Current.CancellationToken);

        var track = CreateTrack();

        host.Provider.DetailsBehavior = (_, _) =>
            Task.FromResult<MusicTrack?>(track);

        using var response = await host.Client.GetAsync(
            $"{DetailsUrl}?provider=audius&trackId=D7KyD",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await ReadJsonAsync(response);

        Assert.Equal(
            "Example Track",
            json.GetProperty("title").GetString());

        Assert.Equal(
            "D7KyD",
            json.GetProperty("reference")
                .GetProperty("trackId").GetString());

        Assert.Equal(
            JsonValueKind.Null,
            json.GetProperty("album").ValueKind);

        Assert.Equal("D7KyD", host.Provider.LastTrackId);
        Assert.Equal(1, host.Provider.DetailsCalls);
    }

    [Fact]
    public async Task Details_UnknownTrack_Returns404()
    {
        using var host = new MusicApiTestHost();

        await host.CreateGuestSessionAsync(
            TestContext.Current.CancellationToken);

        using var response = await host.Client.GetAsync(
            $"{DetailsUrl}?provider=audius&trackId=Unknown123",
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var json = await ReadJsonAsync(response);

        Assert.Equal(
            "TrackNotFound",
            json.GetProperty("code").GetString());

        Assert.Equal(1, host.Provider.DetailsCalls);
    }

    [Fact]
    public async Task Details_UnsupportedProvider_Returns400()
    {
        using var host = new MusicApiTestHost();

        await host.CreateGuestSessionAsync(
            TestContext.Current.CancellationToken);

        using var response = await host.Client.GetAsync(
            $"{DetailsUrl}?provider=spotify&trackId=D7KyD",
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);

        var json = await ReadJsonAsync(response);

        Assert.Equal(
            "UnsupportedMusicProvider",
            json.GetProperty("code").GetString());

        Assert.Equal(0, host.Provider.DetailsCalls);
    }

    [Theory]
    [InlineData("?provider=audius")]
    [InlineData("?trackId=D7KyD")]
    [InlineData("?provider=audius&trackId=invalid%2Fid")]
    public async Task Details_InvalidParameters_Returns400(
        string parameters)
    {
        using var host = new MusicApiTestHost();

        await host.CreateGuestSessionAsync(
            TestContext.Current.CancellationToken);

        using var response = await host.Client.GetAsync(
            DetailsUrl + parameters,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);

        var json = await ReadJsonAsync(response);

        Assert.Equal(
            "Validation",
            json.GetProperty("code").GetString());

        Assert.Equal(0, host.Provider.DetailsCalls);
    }

    [Theory]
    [InlineData(
        MusicProviderFailureKind.Unavailable,
        "MusicProviderUnavailable")]
    [InlineData(
        MusicProviderFailureKind.RateLimited,
        "MusicProviderRateLimited")]
    [InlineData(
        MusicProviderFailureKind.AccessDenied,
        "MusicProviderUnavailable")]
    [InlineData(
        MusicProviderFailureKind.InvalidResponse,
        "MusicProviderUnavailable")]
    public async Task Search_ProviderFailure_Returns503(
        MusicProviderFailureKind failureKind,
        string expectedCode)
    {
        using var host = new MusicApiTestHost();

        await host.CreateGuestSessionAsync(
            TestContext.Current.CancellationToken);

        host.Provider.SearchBehavior = (_, _, _) =>
            Task.FromException<IReadOnlyList<MusicTrack>>(
                new MusicProviderException(
                    failureKind,
                    "Sensitive provider error details."));

        using var response = await host.Client.GetAsync(
            $"{SearchUrl}?query=electronic",
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);

        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        Assert.DoesNotContain(
            "Sensitive provider error details.",
            body);

        using var document = JsonDocument.Parse(body);

        Assert.Equal(
            expectedCode,
            document.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Details_ProviderFailure_Returns503()
    {
        using var host = new MusicApiTestHost();

        await host.CreateGuestSessionAsync(
            TestContext.Current.CancellationToken);

        host.Provider.DetailsBehavior = (_, _) =>
            Task.FromException<MusicTrack?>(
                new MusicProviderException(
                    MusicProviderFailureKind.Unavailable,
                    "Simulated Audius failure."));

        using var response = await host.Client.GetAsync(
            $"{DetailsUrl}?provider=audius&trackId=D7KyD",
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);

        var json = await ReadJsonAsync(response);

        Assert.Equal(
            "MusicProviderUnavailable",
            json.GetProperty("code").GetString());
    }

    private static MusicTrack CreateTrack()
    {
        return new MusicTrack(
            new TrackReference("audius", "D7KyD"),
            "Example Track",
            new[] { "Example Artist" },
            album: null,
            durationMs: 180000,
            artworkUrl: "https://images.example.com/cover.jpg",
            externalUrl: "https://audius.co/artist/example");
    }

    private static async Task<JsonElement> ReadJsonAsync(
        HttpResponseMessage response)
    {
        return await response.Content.ReadFromJsonAsync<JsonElement>(
            cancellationToken:
                TestContext.Current.CancellationToken);
    }
}