using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VibyRoom.Application.Abstractions.Repositories;
using VibyRoom.Infrastructure.Persistence.Repositories;

namespace VibyRoom.Infrastructure.Persistence;

public static class PersistenceExtensions
{
    public static IServiceCollection AddInfrastructurePersistance(
        this IServiceCollection services,
        string? connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<VibyRoomDbContext>(
            options => options.UseNpgsql(connectionString));

        services.AddScoped<IRoomRepository, RoomRepository>();

        services.AddScoped<
            IRoomMusicSessionRepository,
            RoomMusicSessionRepository>();

        services.AddScoped<
            IRoomQueueWriteExecutor,
            RoomQueueWriteExecutor>();

        return services;
    }
}