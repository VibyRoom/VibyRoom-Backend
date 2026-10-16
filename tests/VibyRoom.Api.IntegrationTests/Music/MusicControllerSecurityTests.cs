using System.Net;
using System.Text.Json;
using VibyRoom.Application.Contracts.Music;
using Xunit;

namespace VibyRoom.Api.IntegrationTests.Music;

public sealed class MusicControllerSecurityTests
{
    private const string SearchUrl =
        "/api/v1/music/tracks/search?query=electronic";

    private const string DetailsUrl =
        "/api/v1/music/tracks/details" +
        "?provider=audius&trackId=D7KyD";

    [Theory]
    [InlineData(SearchUrl)]
    [InlineData(DetailsUrl)]
    public async Task MusicEndpoints_WithoutCookie_Return401(
        string url)
    {
        using var host = new MusicApiTestHost();

        using var response = await host.Client.GetAsync(
            url,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);

        Assert.Null(response.Headers.Location);

        Assert.Equal(0, host.Provider.SearchCalls);
        Assert.Equal(0, host.Provider.DetailsCalls);
    }

    [Fact]
    public async Task Search_ForgedGuestId_DoesNotAuthenticate()
    {
        using var host = new MusicApiTestHost();

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            SearchUrl);

        request.Headers.Add(
            "X-Guest-Id",
            Guid.NewGuid().ToString("D"));

        using var response = await host.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);

        Assert.Equal(0, host.Provider.SearchCalls);
    }

    [Fact]
    public async Task Search_AuthenticatedGet_DoesNotRequireCsrf()
    {
        using var host = new MusicApiTestHost();

        await host.CreateGuestSessionAsync(
            TestContext.Current.CancellationToken);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            SearchUrl);

        using var response = await host.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, host.Provider.SearchCalls);
    }

    [Fact]
    public async Task Search_ExceedsLocalRateLimit_Returns429()
    {
        using var host = new MusicApiTestHost();

        await host.CreateGuestSessionAsync(
            TestContext.Current.CancellationToken);

        var permitLimit = host.MusicCatalogPermitLimit;

        for (var attempt = 0; attempt < permitLimit; attempt++)
        {
            using var response = await host.Client.GetAsync(
                SearchUrl,
                TestContext.Current.CancellationToken);

            Assert.Equal(
                HttpStatusCode.OK,
                response.StatusCode);
        }

        using var rejected = await host.Client.GetAsync(
            SearchUrl,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            rejected.StatusCode);

        Assert.Equal(
            permitLimit,
            host.Provider.SearchCalls);

        Assert.Equal(
            "application/problem+json",
            rejected.Content.Headers.ContentType?.MediaType);

        var body = await rejected.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        using var document = JsonDocument.Parse(body);

        Assert.Equal(
            "RateLimitExceeded",
            document.RootElement
                .GetProperty("code").GetString());
    }

    [Fact]
    public async Task Search_UnexpectedException_DoesNotLeakDetails()
    {
        using var host = new MusicApiTestHost();

        await host.CreateGuestSessionAsync(
            TestContext.Current.CancellationToken);

        const string secret = "sensitive-internal-information";

        host.Provider.SearchBehavior = (_, _, _) =>
            Task.FromException<IReadOnlyList<MusicTrack>>(
                new InvalidOperationException(secret));

        using var response = await host.Client.GetAsync(
            SearchUrl,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.InternalServerError,
            response.StatusCode);

        var body = await response.Content.ReadAsStringAsync(
            TestContext.Current.CancellationToken);

        Assert.DoesNotContain(secret, body);

        using var document = JsonDocument.Parse(body);

        Assert.Equal(
            "InternalServerError",
            document.RootElement
                .GetProperty("code").GetString());
    }
}