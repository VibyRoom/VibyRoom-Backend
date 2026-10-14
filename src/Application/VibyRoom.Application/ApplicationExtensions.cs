using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VibyRoom.Application.Abstractions.Rooms;
using VibyRoom.Application.Configuration;
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

        services.AddSingleton<TimeProvider>(
            TimeProvider.System);

        services.AddScoped<RoomApplicationService>();

        services.AddScoped<
            IRoomMembershipQuery,
            RoomMembershipQuery>();

        return services;
    }
}