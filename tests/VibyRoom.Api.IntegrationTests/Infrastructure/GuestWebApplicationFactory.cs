using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace VibyRoom.Api.IntegrationTests.Infrastructure;

public sealed class GuestWebApplicationFactory
    : WebApplicationFactory<Program>
{
    private readonly TimeProvider _timeProvider;
    private readonly bool _ownsKeysDirectory;

    public string KeysDirectory { get; }

    public GuestWebApplicationFactory(
        TimeProvider? timeProvider = null,
        string? keysDirectory = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;

        if (keysDirectory is null)
        {
            KeysDirectory = Directory
                .CreateTempSubdirectory("vibyroom-tests-")
                .FullName;

            _ownsKeysDirectory = true;
        }
        else
        {
            KeysDirectory = Path.GetFullPath(keysDirectory);
            Directory.CreateDirectory(KeysDirectory);

            _ownsKeysDirectory = false;
        }
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] =
                        "Host=127.0.0.1;" +
                        "Database=vibyroom_integration_tests;" +
                        "Username=test;" +
                        "Password=not-used",

                    ["DataProtection:ApplicationName"] =
                        "VibyRoom.IntegrationTests",

                    ["DataProtection:KeysDirectory"] =
                        KeysDirectory
                });
        });

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        builder.ConfigureTestServices(services =>
        {
            services
                .AddControllers()
                .AddApplicationPart(
                    typeof(TestGuestController).Assembly);

            services.PostConfigureAll<CookieAuthenticationOptions>(
                options =>
                {
                    options.TimeProvider = _timeProvider;
                });
        });
    }

    public HttpClient CreateHttpsClient(
        bool handleCookies = true)
    {
        return CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = handleCookies,
            AllowAutoRedirect = false
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing &&
            _ownsKeysDirectory &&
            Directory.Exists(KeysDirectory))
        {
            Directory.Delete(KeysDirectory, recursive: true);
        }
    }
}