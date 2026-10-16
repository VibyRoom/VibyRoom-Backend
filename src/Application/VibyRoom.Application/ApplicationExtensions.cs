using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VibyRoom.Application.Abstractions.Rooms;
using VibyRoom.Application.Configuration;
using VibyRoom.Application.Music;
using VibyRoom.Application.Playback;
using VibyRoom.Application.Queue;
using VibyRoom.Application.Rooms;

namespace VibyRoom.Application;

public static class ApplicationExtensions
{
    public static IServiceCollection AddApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<RoomSettings>()
            .Bind(configuration.GetSection(RoomSettings.SectionName))
            .Validate(
                settings => settings.Capacity > 0,
                "Rooms:Capacity must be greater than zero.")
            .ValidateOnStart();

        services.AddSingleton(
            serviceProvider => serviceProvider
                .GetRequiredService<IOptions<RoomSettings>>()
                .Value);

        services
            .AddOptions<RoomQueueSettings>()
            .Bind(configuration.GetSection(RoomQueueSettings.SectionName))
            .Validate(
                settings =>
                    settings.MaxPendingItems is >= 1 and <= 100,
                "RoomQueue:MaxPendingItems must be between 1 and 100.")
            .ValidateOnStart();

        services.AddSingleton(
            serviceProvider => serviceProvider
                .GetRequiredService<IOptions<RoomQueueSettings>>()
                .Value);

        services.AddSingleton<TimeProvider>(
            TimeProvider.System);

        services.AddScoped<RoomApplicationService>();
        services.AddScoped<RoomQueueApplicationService>();
        services.AddScoped<RoomPlaybackApplicationService>();
        services.AddScoped<MusicStreamApplicationService>();

        services.AddScoped<
            IRoomMembershipQuery,
            RoomMembershipQuery>();

        services.AddScoped<MusicCatalogService>();

        return services;
    }
}