using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VibyRoom.Api.Contracts;
using VibyRoom.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace VibyRoom.Api.IntegrationTests;

public sealed class GuestAntiforgeryTests
{
    private const string MutationUrl =
        "/api/v1/test/guest/mutation";

    private const string SessionUrl =
        "/api/v1/guest-sessions";

    private const string AntiforgeryUrl =
        "/api/v1/security/antiforgery";

    private const string CsrfHeader = "X-CSRF-TOKEN";

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Mutation_WithoutCsrfToken_Returns400(
        string method)
    {
        using var factory = new GuestWebApplicationFactory();
        using var client = factory.CreateHttpsClient();

        await GetCsrfTokenAsync(client);

        using var response = await SendMutationAsync(
            client,
            method);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Mutation_WithInvalidCsrfToken_Returns400(
        string method)
    {
        using var factory = new GuestWebApplicationFactory();
        using var client = factory.CreateHttpsClient();

        await GetCsrfTokenAsync(client);

        using var response = await SendMutationAsync(
            client,
            method,
            "invalid-csrf-token");

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Mutation_WithTokenButWithoutCookie_Returns400(
        string method)
    {
        using var factory = new GuestWebApplicationFactory();

        using var firstClient = factory.CreateHttpsClient();

        var csrfToken = await GetCsrfTokenAsync(firstClient);

        using var secondClient = factory.CreateHttpsClient(
            handleCookies: false);

        using var response = await SendMutationAsync(
            secondClient,
            method,
            csrfToken);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Mutation_WithAnotherClientsToken_Returns400(
        string method)
    {
        using var factory = new GuestWebApplicationFactory();

        using var firstClient = factory.CreateHttpsClient();
        using var secondClient = factory.CreateHttpsClient();

        var firstToken = await GetCsrfTokenAsync(firstClient);

        await GetCsrfTokenAsync(secondClient);

        using var response = await SendMutationAsync(
            secondClient,
            method,
            firstToken);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Mutation_WithValidAnonymousToken_Returns200(
        string method)
    {
        using var factory = new GuestWebApplicationFactory();
        using var client = factory.CreateHttpsClient();

        var csrfToken = await GetCsrfTokenAsync(client);

        using var response = await SendMutationAsync(
            client,
            method,
            csrfToken);

        await AssertSuccessfulMutationAsync(response);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Mutation_WithAuthenticatedGuestToken_Returns200(
        string method)
    {
        using var factory = new GuestWebApplicationFactory();
        using var client = factory.CreateHttpsClient();

        await CreateGuestSessionAsync(client);

        var authenticatedToken = await GetCsrfTokenAsync(client);

        using var response = await SendMutationAsync(
            client,
            method,
            authenticatedToken);

        await AssertSuccessfulMutationAsync(response);
    }

    private static async Task CreateGuestSessionAsync(
        HttpClient client)
    {
        var csrfToken = await GetCsrfTokenAsync(client);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            SessionUrl);

        request.Headers.Add(CsrfHeader, csrfToken);

        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var session = await response.Content
            .ReadFromJsonAsync<GuestSessionResponse>(
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.NotNull(session);
        Assert.NotEqual(Guid.Empty, session.GuestId);
    }

    private static async Task<string> GetCsrfTokenAsync(
        HttpClient client)
    {
        using var response = await client.GetAsync(
            AntiforgeryUrl,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var token = await response.Content
            .ReadFromJsonAsync<AntiforgeryTokenResponse>(
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.NotNull(token);
        Assert.Equal(CsrfHeader, token.HeaderName);
        Assert.False(string.IsNullOrWhiteSpace(token.RequestToken));

        return token.RequestToken;
    }

    private static async Task<HttpResponseMessage> SendMutationAsync(
        HttpClient client,
        string method,
        string? csrfToken = null)
    {
        using var request = new HttpRequestMessage(
            new HttpMethod(method),
            MutationUrl);

        if (csrfToken is not null)
        {
            request.Headers.Add(CsrfHeader, csrfToken);
        }

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static async Task AssertSuccessfulMutationAsync(
        HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content
            .ReadFromJsonAsync<JsonElement>(
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.True(
            payload.GetProperty("accepted").GetBoolean());
    }
}