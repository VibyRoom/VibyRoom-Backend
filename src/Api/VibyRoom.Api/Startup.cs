using VibyRoom.Api.Authentication;
using VibyRoom.Application.Abstractions.Authentication;
using VibyRoom.Infrastructure.Persistence;

namespace VibyRoom.Api;

public sealed class Startup
{
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public Startup(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        _configuration = configuration
                         ?? throw new ArgumentNullException(nameof(configuration));

        _environment = environment
                       ?? throw new ArgumentNullException(nameof(environment));
    }

    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddControllers();
        services.AddOpenApi();

        services.AddHttpContextAccessor();

        services.AddScoped<
            ICurrentGuest,
            HttpContextCurrentGuest>();

        services.AddGuestDataProtection(
            _configuration,
            _environment);

        services.AddGuestAuthentication();
        services.AddGuestAntiforgery();

        var connectionString = _configuration
            .GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'DefaultConnection' is not configured.");
        }

        services.AddInfrastructurePersistance(connectionString);
    }

    public void Configure(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.UseHttpsRedirection();

        app.UseRouting();

        app.UseAuthentication();

        app.UseMiddleware<GuestAntiforgeryMiddleware>();

        app.UseAuthorization();

        app.MapControllers();
    }
}