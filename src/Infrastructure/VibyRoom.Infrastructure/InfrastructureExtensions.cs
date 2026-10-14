using Microsoft.Extensions.DependencyInjection;
using VibyRoom.Application.Abstractions.Invitations;
using VibyRoom.Infrastructure.Invitations;

namespace VibyRoom.Infrastructure;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<
            IInviteCodeGenerator,
            CryptographicInviteCodeGenerator>();

        return services;
    }
}