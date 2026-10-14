using System.Net;
using System.Net.Http.Json;
using VibyRoom.Api.Contracts;
using VibyRoom.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace VibyRoom.Api.IntegrationTests;

public sealed class GuestSessionTests
{
    private const string SessionUrl = "/api/v1/guest-sessions";
    private const string AntiforgeryUrl = "/api/v1/security/antiforgery";
    private const string CsrfHeader = "X-CSRF-TOKEN";

    [Fact]
    public async Task GetAntiforgeryToken_WithoutSession_ReturnsTokenAndCookie()
    {
        using var factory = new GuestWebApplicationFactory();
        using var client = factory.CreateHttpsClient();

        using var response = await client.GetAsync(AntiforgeryUrl, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content
            .ReadFromJsonAsync<AntiforgeryTokenResponse>(cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(payload);
        Assert.False(string.IsNullOrWhiteSpace(payload.RequestToken));
        Assert.Equal(CsrfHeader, payload.HeaderName);

        var cookie = Assert.Single(
            GetCookies(response),
            value => value.StartsWith(
                "VibyRoom.Antiforgery=",
                StringComparison.Ordinal));

        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateSession_WithoutCsrfToken_ReturnsBadRequest()
    {
        using var factory = new GuestWebApplicationFactory();
        using var client = factory.CreateHttpsClient();

        using var response = await PostSessionAsync(client);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        Assert.DoesNotContain(
            GetCookies(response),
            cookie => cookie.StartsWith(
                "VibyRoom.Guest=",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateSession_WithInvalidCsrfToken_ReturnsBadRequest()
    {
        using var factory = new GuestWebApplicationFactory();
        using var client = factory.CreateHttpsClient();

        await GetCsrfTokenAsync(client);

        using var response = await PostSessionAsync(
            client,
            "invalid-csrf-token");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSession_WithValidCsrfToken_CreatesPersistentGuest()
    {
        using var factory = new GuestWebApplicationFactory();
        using var client = factory.CreateHttpsClient();

        var token = await GetCsrfTokenAsync(client);

        var beforeRequest = DateTimeOffset.UtcNow;

        using var response = await PostSessionAsync(client, token);

        var afterRequest = DateTimeOffset.UtcNow;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var session = await ReadSessionAsync(response);

        Assert.NotEqual(Guid.Empty, session.GuestId);

        Assert.InRange(
            session.ExpiresAtUtc,
            beforeRequest.AddDays(29),
            afterRequest.AddDays(31));

        var cookie = Assert.Single(
            GetCookies(response),
            value => value.StartsWith(
                "VibyRoom.Guest=",
                StringComparison.Ordinal));

        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expires=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateSession_WithExistingSession_PreservesGuestId()
    {
        using var factory = new GuestWebApplicationFactory();
        using var client = factory.CreateHttpsClient();

        var initialToken = await GetCsrfTokenAsync(client);

        using var firstResponse = await PostSessionAsync(
            client,
            initialToken);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var firstSession = await ReadSessionAsync(firstResponse);

        var authenticatedToken = await GetCsrfTokenAsync(client);

        using var secondResponse = await PostSessionAsync(
            client,
            authenticatedToken);

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        var secondSession = await ReadSessionAsync(secondResponse);

        Assert.Equal(firstSession.GuestId, secondSession.GuestId);
        Assert.Equal(
            firstSession.ExpiresAtUtc.ToUnixTimeSeconds(),
            secondSession.ExpiresAtUtc.ToUnixTimeSeconds());

        Assert.DoesNotContain(
            GetCookies(secondResponse),
            cookie => cookie.StartsWith(
                "VibyRoom.Guest=",
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateSession_ForIndependentClients_ReturnsDifferentGuestIds()
    {
        using var factory = new GuestWebApplicationFactory();

        using var firstClient = factory.CreateHttpsClient();
        using var secondClient = factory.CreateHttpsClient();

        var firstToken = await GetCsrfTokenAsync(firstClient);
        var secondToken = await GetCsrfTokenAsync(secondClient);

        using var firstResponse = await PostSessionAsync(
            firstClient,
            firstToken);

        using var secondResponse = await PostSessionAsync(
            secondClient,
            secondToken);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        var firstSession = await ReadSessionAsync(firstResponse);
        var secondSession = await ReadSessionAsync(secondResponse);

        Assert.NotEqual(firstSession.GuestId, secondSession.GuestId);
    }

    [Fact]
    public async Task CreateSession_WithSpoofedGuestId_IgnoresClientIdentity()
    {
        using var factory = new GuestWebApplicationFactory();
        using var client = factory.CreateHttpsClient();

        var initialToken = await GetCsrfTokenAsync(client);

        using var initialResponse = await PostSessionAsync(
            client,
            initialToken);

        Assert.Equal(HttpStatusCode.OK, initialResponse.StatusCode);

        var originalSession = await ReadSessionAsync(initialResponse);

        var authenticatedToken = await GetCsrfTokenAsync(client);
        var forgedGuestId = Guid.NewGuid();

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{SessionUrl}?guestId={forgedGuestId:D}");

        request.Headers.Add(CsrfHeader, authenticatedToken);
        request.Headers.Add("X-Guest-Id", forgedGuestId.ToString("D"));

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var session = await ReadSessionAsync(response);

        Assert.Equal(originalSession.GuestId, session.GuestId);
        Assert.NotEqual(forgedGuestId, session.GuestId);
    }

    [Fact]
    public async Task CreateSession_WithAnonymousCsrfTokenAfterSignIn_ReturnsBadRequest()
    {
        using var factory = new GuestWebApplicationFactory();
        using var client = factory.CreateHttpsClient();

        var anonymousToken = await GetCsrfTokenAsync(client);

        using var firstResponse = await PostSessionAsync(
            client,
            anonymousToken);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        using var secondResponse = await PostSessionAsync(
            client,
            anonymousToken);

        Assert.Equal(HttpStatusCode.BadRequest, secondResponse.StatusCode);
    }

    private static async Task<string> GetCsrfTokenAsync(
        HttpClient client)
    {
        using var response = await client.GetAsync(AntiforgeryUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content
            .ReadFromJsonAsync<AntiforgeryTokenResponse>();

        Assert.NotNull(payload);
        Assert.Equal(CsrfHeader, payload.HeaderName);
        Assert.False(string.IsNullOrWhiteSpace(payload.RequestToken));

        return payload.RequestToken;
    }

    private static async Task<HttpResponseMessage> PostSessionAsync(
        HttpClient client,
        string? csrfToken = null)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            SessionUrl);

        if (csrfToken is not null)
        {
            request.Headers.Add(CsrfHeader, csrfToken);
        }

        return await client.SendAsync(request);
    }

    private static async Task<GuestSessionResponse> ReadSessionAsync(
        HttpResponseMessage response)
    {
        var session = await response.Content
            .ReadFromJsonAsync<GuestSessionResponse>();

        Assert.NotNull(session);

        return session;
    }

    private static string[] GetCookies(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues(
            "Set-Cookie",
            out var values)
            ? values.ToArray()
            : [];
    }
}