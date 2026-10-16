using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using VibyRoom.Api.Contracts;
using VibyRoom.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace VibyRoom.Api.IntegrationTests;

public sealed class GuestSessionExpirationTests
{
    private const string SessionUrl =
        "/api/v1/guest-sessions";

    private const string AntiforgeryUrl =
        "/api/v1/security/antiforgery";

    private const string ProtectedUrl =
        "/api/v1/test/guest/protected";

    private const string IdentityUrl =
        "/api/v1/test/guest/identity";

    private const string CsrfHeader = "X-CSRF-TOKEN";

    private const string GuestCookieName = "VibyRoom.Guest=";

    [Fact]
    public async Task ExpiredSession_IsRejectedAndNewSessionGetsDifferentGuestId()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);

        using var factory = new GuestWebApplicationFactory(timeProvider);
        using var client = factory.CreateHttpsClient();

        var originalSession = await CreateSessionAsync(client);

        timeProvider.Advance(TimeSpan.FromDays(31));

        using var protectedResponse = await client.GetAsync(
            ProtectedUrl,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            protectedResponse.StatusCode);

        Assert.Null(protectedResponse.Headers.Location);

        using var identityResponse = await client.GetAsync(
            IdentityUrl,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, identityResponse.StatusCode);

        var identity = await identityResponse.Content
            .ReadFromJsonAsync<JsonElement>(
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.Equal(
            JsonValueKind.Null,
            identity.GetProperty("guestId").ValueKind);

        var newSession = await CreateSessionAsync(client);

        Assert.NotEqual(Guid.Empty, newSession.GuestId);
        Assert.NotEqual(originalSession.GuestId, newSession.GuestId);

        Assert.True(
            newSession.ExpiresAtUtc > originalSession.ExpiresAtUtc);
    }

    [Fact]
    public async Task ActiveSession_BeforeHalfLifetime_DoesNotRenewCookie()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);

        using var factory = new GuestWebApplicationFactory(timeProvider);
        using var client = factory.CreateHttpsClient();

        var originalSession = await CreateSessionAsync(client);

        var csrfToken = await GetCsrfTokenAsync(client);

        timeProvider.Advance(TimeSpan.FromDays(14));

        using var response = await PostSessionAsync(
            client,
            csrfToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var currentSession = await ReadSessionAsync(response);

        Assert.Equal(
            originalSession.GuestId,
            currentSession.GuestId);

        Assert.Equal(
            originalSession.ExpiresAtUtc.ToUnixTimeSeconds(),
            currentSession.ExpiresAtUtc.ToUnixTimeSeconds());

        Assert.DoesNotContain(
            GetCookies(response),
            cookie => cookie.StartsWith(
                GuestCookieName,
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task ActiveSession_AfterHalfLifetime_RenewsCookieAndPreservesGuestId()
    {
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);

        using var factory = new GuestWebApplicationFactory(timeProvider);
        using var client = factory.CreateHttpsClient();

        var originalSession = await CreateSessionAsync(client);

        timeProvider.Advance(TimeSpan.FromDays(16));

        using var renewalResponse = await client.GetAsync(
            ProtectedUrl,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.OK,
            renewalResponse.StatusCode);

        var renewedIdentity = await renewalResponse.Content
            .ReadFromJsonAsync<JsonElement>(
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.Equal(
            originalSession.GuestId,
            renewedIdentity.GetProperty("guestId").GetGuid());

        var renewedCookie = Assert.Single(
            GetCookies(renewalResponse),
            cookie => cookie.StartsWith(
                GuestCookieName,
                StringComparison.Ordinal));

        Assert.Contains(
            "expires=",
            renewedCookie,
            StringComparison.OrdinalIgnoreCase);

        var csrfToken = await GetCsrfTokenAsync(client);

        using var sessionResponse = await PostSessionAsync(
            client,
            csrfToken);

        Assert.Equal(
            HttpStatusCode.OK,
            sessionResponse.StatusCode);

        var renewedSession = await ReadSessionAsync(sessionResponse);

        Assert.Equal(
            originalSession.GuestId,
            renewedSession.GuestId);

        Assert.True(
            renewedSession.ExpiresAtUtc > originalSession.ExpiresAtUtc);

        var expectedExpiration = timeProvider
            .GetUtcNow()
            .AddDays(30);

        Assert.InRange(
            renewedSession.ExpiresAtUtc,
            expectedExpiration.AddSeconds(-2),
            expectedExpiration.AddSeconds(2));

        timeProvider.Advance(TimeSpan.FromDays(15));

        using var subsequentResponse = await client.GetAsync(
            ProtectedUrl,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.OK,
            subsequentResponse.StatusCode);

        var subsequentIdentity = await subsequentResponse.Content
            .ReadFromJsonAsync<JsonElement>(
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.Equal(
            originalSession.GuestId,
            subsequentIdentity.GetProperty("guestId").GetGuid());
    }

    private static async Task<GuestSessionResponse> CreateSessionAsync(
        HttpClient client)
    {
        var csrfToken = await GetCsrfTokenAsync(client);

        using var response = await PostSessionAsync(
            client,
            csrfToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await ReadSessionAsync(response);
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
        Assert.False(string.IsNullOrWhiteSpace(payload.RequestToken));
        Assert.Equal(CsrfHeader, payload.HeaderName);

        return payload.RequestToken;
    }

    private static async Task<HttpResponseMessage> PostSessionAsync(
        HttpClient client,
        string csrfToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            SessionUrl);

        request.Headers.Add(CsrfHeader, csrfToken);

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static async Task<GuestSessionResponse> ReadSessionAsync(
        HttpResponseMessage response)
    {
        var session = await response.Content
            .ReadFromJsonAsync<GuestSessionResponse>(
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.NotNull(session);
        Assert.NotEqual(Guid.Empty, session.GuestId);

        return session;
    }

    private static string[] GetCookies(
        HttpResponseMessage response)
    {
        return response.Headers.TryGetValues(
            "Set-Cookie",
            out var values)
            ? values.ToArray()
            : [];
    }
}