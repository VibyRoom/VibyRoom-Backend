using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VibyRoom.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace VibyRoom.Api.IntegrationTests;

public sealed class GuestAuthenticationTicketTests
{
    private const string AuthenticationScheme = "Cookie";
    private const string GuestCookieName = "VibyRoom.Guest";

    private const string ProtectedUrl =
        "/api/v1/test/guest/protected";

    private const string IdentityUrl =
        "/api/v1/test/guest/identity";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Ticket_WithInvalidGuestId_IsRejected(
        string? guestIdValue)
    {
        using var factory = new GuestWebApplicationFactory();

        Claim[] claims = guestIdValue is null
            ? []
            : [new Claim(ClaimTypes.NameIdentifier, guestIdValue)];

        var cookie = CreateSignedCookie(factory, claims);

        await AssertAuthenticationRejectedAsync(factory, cookie);
    }

    [Fact]
    public async Task Ticket_WithDuplicateGuestIdClaims_IsRejected()
    {
        using var factory = new GuestWebApplicationFactory();

        var claims = new[]
        {
            new Claim(
                ClaimTypes.NameIdentifier,
                Guid.NewGuid().ToString("D")),

            new Claim(
                ClaimTypes.NameIdentifier,
                Guid.NewGuid().ToString("D"))
        };

        var cookie = CreateSignedCookie(factory, claims);

        await AssertAuthenticationRejectedAsync(factory, cookie);
    }

    [Fact]
    public async Task Ticket_WithWrongIdentityAuthenticationType_IsRejected()
    {
        using var factory = new GuestWebApplicationFactory();

        var claims = new[]
        {
            new Claim(
                ClaimTypes.NameIdentifier,
                Guid.NewGuid().ToString("D"))
        };

        var cookie = CreateSignedCookie(
            factory,
            claims,
            authenticationType: "OtherScheme");

        await AssertAuthenticationRejectedAsync(factory, cookie);
    }

    [Fact]
    public async Task Ticket_WithValidGuestIdentity_IsAccepted()
    {
        using var factory = new GuestWebApplicationFactory();

        var expectedGuestId = Guid.NewGuid();

        var claims = new[]
        {
            new Claim(
                ClaimTypes.NameIdentifier,
                expectedGuestId.ToString("D"))
        };

        var cookie = CreateSignedCookie(factory, claims);

        using var client = factory.CreateHttpsClient(
            handleCookies: false);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            ProtectedUrl);

        request.Headers.TryAddWithoutValidation(
            "Cookie",
            cookie);

        using var response = await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var actualGuestId = await ReadGuestIdAsync(response);

        Assert.Equal(expectedGuestId, actualGuestId);
    }

    private static string CreateSignedCookie(
        GuestWebApplicationFactory factory,
        IEnumerable<Claim> claims,
        string authenticationType = AuthenticationScheme)
    {
        var options = factory.Services
            .GetRequiredService<
                IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(AuthenticationScheme);

        var issuedAtUtc = DateTimeOffset.UtcNow;

        var identity = new ClaimsIdentity(
            claims,
            authenticationType);

        var principal = new ClaimsPrincipal(identity);

        var properties = new AuthenticationProperties
        {
            IsPersistent = true,
            IssuedUtc = issuedAtUtc,
            ExpiresUtc = issuedAtUtc.AddDays(30)
        };

        var ticket = new AuthenticationTicket(
            principal,
            properties,
            AuthenticationScheme);

        var protectedValue = options.TicketDataFormat
            .Protect(ticket);

        return $"{GuestCookieName}={protectedValue}";
    }

    private static async Task AssertAuthenticationRejectedAsync(
        GuestWebApplicationFactory factory,
        string cookie)
    {
        using var client = factory.CreateHttpsClient(
            handleCookies: false);

        using var protectedRequest = new HttpRequestMessage(
            HttpMethod.Get,
            ProtectedUrl);

        protectedRequest.Headers.TryAddWithoutValidation(
            "Cookie",
            cookie);

        using var protectedResponse = await client.SendAsync(
            protectedRequest,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            protectedResponse.StatusCode);

        Assert.Null(protectedResponse.Headers.Location);

        using var identityRequest = new HttpRequestMessage(
            HttpMethod.Get,
            IdentityUrl);

        identityRequest.Headers.TryAddWithoutValidation(
            "Cookie",
            cookie);

        using var identityResponse = await client.SendAsync(
            identityRequest,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            HttpStatusCode.OK,
            identityResponse.StatusCode);

        var guestId = await ReadGuestIdAsync(identityResponse);

        Assert.Null(guestId);
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