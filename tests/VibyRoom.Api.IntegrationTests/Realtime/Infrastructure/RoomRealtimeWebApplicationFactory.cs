using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VibyRoom.Application.Abstractions.Repositories;

namespace VibyRoom.Api.IntegrationTests.Realtime.Infrastructure;

public sealed class RoomRealtimeWebApplicationFactory
    : WebApplicationFactory<Program>
{
    private const string AuthenticationScheme = "Cookie";
    private const string CookieName = "VibyRoom.Guest";

    private readonly string _keysDirectory = Directory
        .CreateTempSubdirectory("vibyroom-realtime-tests-")
        .FullName;

    public InMemoryRealtimeRoomRepository Rooms { get; } = new();

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=127.0.0.1;" +
                        "Database=vibyroom_realtime_tests;" +
                        "Username=test;Password=not-used",

                    ["DataProtection:ApplicationName"] =
                        "VibyRoom.RealtimeTests",

                    ["DataProtection:KeysDirectory"] =
                        _keysDirectory
                });
        });

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(
        IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Realtime:AllowedOrigins:0"] =
                        "https://localhost",

                    ["Realtime:AllowedOrigins:1"] =
                        "https://localhost:7298"
                });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IRoomRepository>();

            services.AddScoped<IRoomRepository>(
                _ => Rooms);
        });
    }

    public string CreateGuestCookie(Guid guestId)
    {
        if (guestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Guest ID cannot be empty.",
                nameof(guestId));
        }

        var options = Services
            .GetRequiredService<
                IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(AuthenticationScheme);

        var now = DateTimeOffset.UtcNow;

        var identity = new ClaimsIdentity(
            [
                new Claim(
                    ClaimTypes.NameIdentifier,
                    guestId.ToString("D"))
            ],
            AuthenticationScheme);

        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = true,
                IssuedUtc = now,
                ExpiresUtc = now.AddDays(30)
            },
            AuthenticationScheme);

        var protectedTicket = options.TicketDataFormat
            .Protect(ticket);

        return $"{CookieName}={protectedTicket}";
    }

    public HubConnection CreateHubConnection(
        string? authenticationCookie = null)
    {
        _ = Server;

        return new HubConnectionBuilder()
            .WithUrl(
                "https://localhost/hubs/rooms",
                options =>
                {
                    options.Transports =
                        HttpTransportType.LongPolling;

                    options.HttpMessageHandlerFactory =
                        _ => Server.CreateHandler();

                    if (!string.IsNullOrWhiteSpace(
                            authenticationCookie))
                    {
                        options.Headers["Cookie"] =
                            authenticationCookie;
                    }
                })
            .Build();
    }

    public HttpClient CreateHttpsClient()
    {
        return CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress =
                    new Uri("https://localhost"),

                HandleCookies = false,
                AllowAutoRedirect = false
            });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && Directory.Exists(_keysDirectory))
        {
            Directory.Delete(
                _keysDirectory,
                recursive: true);
        }
    }
}