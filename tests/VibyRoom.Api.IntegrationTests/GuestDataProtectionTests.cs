using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using VibyRoom.Api.Authentication;
using VibyRoom.Api.Contracts;
using VibyRoom.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace VibyRoom.Api.IntegrationTests;

public sealed class GuestDataProtectionTests
{
    private const string SessionUrl =
        "/api/v1/guest-sessions";

    private const string AntiforgeryUrl =
        "/api/v1/security/antiforgery";

    private const string ProtectedUrl =
        "/api/v1/test/guest/protected";

    private const string GuestCookieName = "VibyRoom.Guest=";

    private const string CsrfHeader = "X-CSRF-TOKEN";

    [Fact]
    public async Task Cookie_WithSharedKeyRing_RemainsValidAfterHostRestart()
    {
        var keysDirectory = Directory
            .CreateTempSubdirectory("vibyroom-shared-keys-")
            .FullName;

        try
        {
            string authenticationCookie;
            Guid expectedGuestId;

            using (var firstFactory = new GuestWebApplicationFactory(
                       keysDirectory: keysDirectory))
            {
                using var firstClient =
                    firstFactory.CreateHttpsClient();

                var csrfToken = await GetCsrfTokenAsync(firstClient);

                using var sessionResponse = await PostSessionAsync(
                    firstClient,
                    csrfToken);

                Assert.Equal(
                    HttpStatusCode.OK,
                    sessionResponse.StatusCode);

                var session = await sessionResponse.Content
                    .ReadFromJsonAsync<GuestSessionResponse>(
                        cancellationToken:
                            TestContext.Current.CancellationToken);

                Assert.NotNull(session);
                Assert.NotEqual(Guid.Empty, session.GuestId);

                expectedGuestId = session.GuestId;

                authenticationCookie =
                    GetAuthenticationCookie(sessionResponse);
            }

            using var secondFactory = new GuestWebApplicationFactory(
                keysDirectory: keysDirectory);

            using var secondClient = secondFactory.CreateHttpsClient(
                handleCookies: false);

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                ProtectedUrl);

            request.Headers.TryAddWithoutValidation(
                "Cookie",
                authenticationCookie);

            using var response = await secondClient.SendAsync(
                request,
                TestContext.Current.CancellationToken);

            Assert.Equal(
                HttpStatusCode.OK,
                response.StatusCode);

            var actualGuestId = await ReadGuestIdAsync(response);

            Assert.Equal(expectedGuestId, actualGuestId);
        }
        finally
        {
            Directory.Delete(keysDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Cookie_WithDifferentKeyRings_IsRejected()
    {
        using var firstFactory = new GuestWebApplicationFactory();
        using var secondFactory = new GuestWebApplicationFactory();

        using var firstClient = firstFactory.CreateHttpsClient();

        var csrfToken = await GetCsrfTokenAsync(firstClient);

        using var sessionResponse = await PostSessionAsync(
            firstClient,
            csrfToken);

        Assert.Equal(
            HttpStatusCode.OK,
            sessionResponse.StatusCode);

        var authenticationCookie =
            GetAuthenticationCookie(sessionResponse);

        using var secondClient = secondFactory.CreateHttpsClient(
            handleCookies: false);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            ProtectedUrl);

        request.Headers.TryAddWithoutValidation(
            "Cookie",
            authenticationCookie);

        using var response = await secondClient.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);

        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public void Production_WithoutKeysDirectory_ThrowsConfigurationException()
    {
        var builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                EnvironmentName = Environments.Production
            });

        builder.Configuration["DataProtection:KeysDirectory"] = "";

        var exception = Assert.Throws<InvalidOperationException>(
            () =>
            {
                builder.Services.AddGuestDataProtection(
                    builder.Configuration,
                    builder.Environment);
            });

        Assert.Contains(
            "DataProtection:KeysDirectory",
            exception.Message);
    }

    [Fact]
    public void Production_WithMissingKeysDirectory_ThrowsConfigurationException()
    {
        var builder = Host.CreateApplicationBuilder(
            new HostApplicationBuilderSettings
            {
                EnvironmentName = Environments.Production
            });

        var missingDirectory = Path.Combine(
            Path.GetTempPath(),
            $"vibyroom-missing-keys-{Guid.NewGuid():N}");

        Assert.False(Directory.Exists(missingDirectory));

        builder.Configuration["DataProtection:KeysDirectory"] =
            missingDirectory;

        var exception = Assert.Throws<InvalidOperationException>(
            () =>
            {
                builder.Services.AddGuestDataProtection(
                    builder.Configuration,
                    builder.Environment);
            });

        Assert.Contains(
            "does not exist",
            exception.Message);
    }

    private static async Task<string> GetCsrfTokenAsync(
        HttpClient client)
    {
        using var response = await client.GetAsync(
            AntiforgeryUrl,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var payload = await response.Content
            .ReadFromJsonAsync<AntiforgeryTokenResponse>(
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.NotNull(payload);
        Assert.False(string.IsNullOrWhiteSpace(payload.RequestToken));

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

    private static string GetAuthenticationCookie(
        HttpResponseMessage response)
    {
        Assert.True(
            response.Headers.TryGetValues(
                "Set-Cookie",
                out var headers));

        var cookieHeader = Assert.Single(
            headers,
            value => value.StartsWith(
                GuestCookieName,
                StringComparison.Ordinal));

        var separatorIndex = cookieHeader.IndexOf(';');

        return separatorIndex >= 0
            ? cookieHeader[..separatorIndex]
            : cookieHeader;
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