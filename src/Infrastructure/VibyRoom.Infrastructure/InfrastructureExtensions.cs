using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VibyRoom.Application.Abstractions.Invitations;
using VibyRoom.Application.Abstractions.Music;
using VibyRoom.Infrastructure.Ai;
using VibyRoom.Infrastructure.Invitations;
using VibyRoom.Infrastructure.Music;
using VibyRoom.Infrastructure.Music.Configuration;

namespace VibyRoom.Infrastructure;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<
            IInviteCodeGenerator,
            CryptographicInviteCodeGenerator>();

        services
            .AddOptions<AudiusProviderOptions>()
            .Bind(configuration.GetSection(
                AudiusProviderOptions.SectionName))
            .Validate(
                options => string.Equals(
                    options.Provider,
                    AudiusProviderOptions.ProviderId,
                    StringComparison.Ordinal),
                "MusicProvider:Provider must be 'audius'.")
            .Validate(
                options =>
                    Uri.TryCreate(
                        options.BaseUrl,
                        UriKind.Absolute,
                        out var uri)
                    && uri.Scheme == Uri.UriSchemeHttps
                    && string.Equals(
                        uri.Host,
                        "api.audius.co",
                        StringComparison.OrdinalIgnoreCase)
                    && uri.AbsolutePath == "/v1/"
                    && uri.IsDefaultPort
                    && string.IsNullOrEmpty(uri.UserInfo)
                    && string.IsNullOrEmpty(uri.Query)
                    && string.IsNullOrEmpty(uri.Fragment),
                "MusicProvider:BaseUrl must be the official Audius API endpoint.")
            .Validate(
                options =>
                    options.TimeoutSeconds is >= 1 and <= 120,
                "MusicProvider:TimeoutSeconds must be between 1 and 120.")
            .ValidateOnStart();

        services.AddHttpClient<
            IMusicCatalogProvider,
            AudiusMusicCatalogClient>();

        services
            .AddHttpClient<
                IMusicStreamProvider,
                AudiusStreamProvider>()
            .ConfigurePrimaryHttpMessageHandler(
                () => new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false
                });

        services
            .AddOptions<GigaChatOptions>()
            .Bind(configuration.GetSection(
                GigaChatOptions.SectionName))
            .Validate(
                options =>
                    options.TimeoutSeconds is >= 5 and <= 120,
                "GigaChat:TimeoutSeconds must be between 5 and 120.")
            .ValidateOnStart();

        services
            .AddHttpClient(GigaChatOptions.OAuthClientName)
            .ConfigureHttpClient((provider, client) =>
            {
                var options = provider
                    .GetRequiredService<IOptions<GigaChatOptions>>()
                    .Value;

                client.Timeout = TimeSpan.FromSeconds(
                    options.TimeoutSeconds);
            })
            .ConfigurePrimaryHttpMessageHandler(provider =>
            {
                var options = provider
                    .GetRequiredService<IOptions<GigaChatOptions>>()
                    .Value;

                return GigaChatHttpHandlerFactory.Create(options);
            });

        services
            .AddHttpClient(GigaChatOptions.ChatClientName)
            .ConfigureHttpClient((provider, client) =>
            {
                var options = provider
                    .GetRequiredService<IOptions<GigaChatOptions>>()
                    .Value;

                client.Timeout = TimeSpan.FromSeconds(
                    options.TimeoutSeconds);
            })
            .ConfigurePrimaryHttpMessageHandler(provider =>
            {
                var options = provider
                    .GetRequiredService<IOptions<GigaChatOptions>>()
                    .Value;

                return GigaChatHttpHandlerFactory.Create(options);
            });

        services.AddSingleton<GigaChatAccessTokenProvider>();

        services.AddSingleton<
            IAiMusicProvider,
            GigaChatClient>();

        return services;
    }
}