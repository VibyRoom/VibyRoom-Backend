using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VibyRoom.Api.Contracts;
using VibyRoom.Api.IntegrationTests.Infrastructure;
using VibyRoom.Application.Abstractions.Music;
using Xunit;

namespace VibyRoom.Api.IntegrationTests.Music;

public sealed class MusicApiTestHost : IDisposable
{
    private readonly GuestWebApplicationFactory _guestFactory;
    private readonly WebApplicationFactory<Program> _musicFactory;

    public FakeMusicCatalogProvider Provider { get; }

    public HttpClient Client { get; }

    public int MusicCatalogPermitLimit { get; }

    public MusicApiTestHost()
    {
        Provider = new FakeMusicCatalogProvider();

        _guestFactory = new GuestWebApplicationFactory();

        _musicFactory = _guestFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMusicCatalogProvider>();

                services.AddSingleton<IMusicCatalogProvider>(
                    Provider);
            });
        });

        Client = _musicFactory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                HandleCookies = true,
                AllowAutoRedirect = false
            });

        var configuration = _musicFactory.Services
            .GetRequiredService<IConfiguration>();

        MusicCatalogPermitLimit = configuration.GetValue<int>(
            "RateLimiting:MusicCatalogPermitLimit");

        if (MusicCatalogPermitLimit <= 0)
        {
            throw new InvalidOperationException(
                "Music catalog rate limit must be positive.");
        }
    }

    public async Task<Guid> CreateGuestSessionAsync(
        CancellationToken cancellationToken = default)
    {
        using var antiforgeryResponse = await Client.GetAsync(
            "/api/v1/security/antiforgery",
            cancellationToken);

        Assert.Equal(
            System.Net.HttpStatusCode.OK,
            antiforgeryResponse.StatusCode);

        var antiforgery = await antiforgeryResponse.Content
            .ReadFromJsonAsync<AntiforgeryTokenResponse>(
                cancellationToken: cancellationToken);

        Assert.NotNull(antiforgery);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/guest-sessions");

        request.Headers.Add(
            antiforgery.HeaderName,
            antiforgery.RequestToken);

        using var response = await Client.SendAsync(
            request,
            cancellationToken);

        Assert.Equal(
            System.Net.HttpStatusCode.OK,
            response.StatusCode);

        var session = await response.Content
            .ReadFromJsonAsync<GuestSessionResponse>(
                cancellationToken: cancellationToken);

        Assert.NotNull(session);
        Assert.NotEqual(Guid.Empty, session.GuestId);

        return session.GuestId;
    }

    public void Dispose()
    {
        Client.Dispose();
        _musicFactory.Dispose();
        _guestFactory.Dispose();
    }
}