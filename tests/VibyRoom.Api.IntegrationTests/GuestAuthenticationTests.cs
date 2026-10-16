using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VibyRoom.Api.Contracts;
using VibyRoom.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace VibyRoom.Api.IntegrationTests;

public sealed class GuestAuthenticationTests
{
    private const string IdentityUrl =
        "/api/v1/test/guest/identity";

    private const string ProtectedUrl =
        "/api/v1/test/guest/protected";

    private const string MutationUrl =
        "/api/v1/test/guest/mutation";

    private const string SessionUrl =
        "/api/v1/guest-sessions";

    private const string AntiforgeryUrl =
        "/api/v1/security/antiforgery";

    private const string CsrfHeader = "X-CSRF-TOKEN";

    [Fact]
    public async Task GetIdentity_WithoutAuthentication_ReturnsNull()
    {
        using var factory = new GuestWebApplicationFactory();
        using var client = factory.CreateHttpsClient();

        using var response = await client.GetAsync(
            IdentityUrl,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var guestId = await ReadGuestIdAsync(response);

        Assert.Null(guestId);
    }

    [Fact]
    public async Task GetProtected_WithoutAuthentication_Returns401()
    {
        using var factory = new GuestWebApplicationFactory();
        using var client = factory.CreateHttpsClient();

        using var response = await client.GetAsync(
            ProtectedUrl,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);

        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task GetProtected_WithValidCookie_ReturnsGuestId()
    {
        using var factory = new GuestWebApplicationFactory();
        using var client = factory.CreateHttpsClient();

        var expectedGuestId = await CreateGuestSessionAsync(client);

        using var response = await client.GetAsync(
            ProtectedUrl,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var actualGuestId = await ReadGuestIdAsync(response);

        Assert.Equal(expectedGuestId, actualGuestId);
    }

    [Fact]
    public async Task GetProtected_WithSpoofedGuestIdWithoutCookie_Returns401()
    {
        using var factory = new GuestWebApplicationFactory();
        using var client = factory.CreateHttpsClient();

        var forgedGuestId = Guid.NewGuid();

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{ProtectedUrl}?guestId={forgedGuestId:D}");

        request.Headers.Add(
            "X-Guest-Id",
            forgedGuestId.ToString("D"));

        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);

        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task GetProtected_WithSpoofedGuestIdAndValidCookie_PreservesIdentity()
    {
        using var factory = new GuestWebApplicationFactory();
        using var client = factory.CreateHttpsClient();

        var expectedGuestId = await CreateGuestSessionAsync(client);
        var forgedGuestId = Guid.NewGuid();

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{ProtectedUrl}?guestId={forgedGuestId:D}");

        request.Headers.Add(
            "X-Guest-Id",
            forgedGuestId.ToString("D"));

        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var actualGuestId = await ReadGuestIdAsync(response);

        Assert.Equal(expectedGuestId, actualGuestId);
        Assert.NotEqual(forgedGuestId, actualGuestId);
    }

    [Fact]
    public async Task GetProtected_WithCorruptedCookie_Returns401()
    {
        using var factory = new GuestWebApplicationFactory();

        using var client = factory.CreateHttpsClient(
            handleCookies: false);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            ProtectedUrl);

        request.Headers.TryAddWithoutValidation(
            "Cookie",
            "VibyRoom.Guest=invalid-authentication-ticket");

        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);

        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task GetIdentity_WithCorruptedCookie_ReturnsNull()
    {
        using var factory = new GuestWebApplicationFactory();

        using var client = factory.CreateHttpsClient(
            handleCookies: false);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            IdentityUrl);

        request.Headers.TryAddWithoutValidation(
            "Cookie",
            "VibyRoom.Guest=invalid-authentication-ticket");

        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var guestId = await ReadGuestIdAsync(response);

        Assert.Null(guestId);
    }

    [Fact]
    public async Task PostMutation_WithoutCsrfToken_Returns400()
    {
        using var factory = new GuestWebApplicationFactory();
        using var client = factory.CreateHttpsClient();

        using var response = await PostMutationAsync(client);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task PostMutation_WithInvalidCsrfToken_Returns400()
    {
        using var factory = new GuestWebApplicationFactory();
        using var client = factory.CreateHttpsClient();

        await GetCsrfTokenAsync(client);

        using var response = await PostMutationAsync(
            client,
            "invalid-csrf-token");

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task PostMutation_WithValidCsrfToken_Returns200()
    {
        using var factory = new GuestWebApplicationFactory();
        using var client = factory.CreateHttpsClient();

        var csrfToken = await GetCsrfTokenAsync(client);

        using var response = await PostMutationAsync(
            client,
            csrfToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<Guid> CreateGuestSessionAsync(
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

        return session.GuestId;
    }

    private static async Task<string> GetCsrfTokenAsync(
        HttpClient client)
    {
        using var response = await client.GetAsync(
            AntiforgeryUrl,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content
            .ReadFromJsonAsync<AntiforgeryTokenResponse>(
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.NotNull(payload);
        Assert.Equal(CsrfHeader, payload.HeaderName);
        Assert.False(string.IsNullOrWhiteSpace(payload.RequestToken));

        return payload.RequestToken;
    }

    private static async Task<HttpResponseMessage> PostMutationAsync(
        HttpClient client,
        string? csrfToken = null)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            MutationUrl);

        if (csrfToken is not null)
        {
            request.Headers.Add(CsrfHeader, csrfToken);
        }

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static async Task<Guid?> ReadGuestIdAsync(
        HttpResponseMessage response)
    {
        var payload = await response.Content
            .ReadFromJsonAsync<JsonElement>(
                cancellationToken:
                    TestContext.Current.CancellationToken);

        var value = payload.GetProperty("guestId");

        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetGuid(),
            _ => throw new JsonException(
                "Unexpected guestId response value.")
        };
    }
}