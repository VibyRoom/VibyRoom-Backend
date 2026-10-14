using VibyRoom.Api.Authentication;
using VibyRoom.Api.Errors;
using VibyRoom.Api.OpenApi;
using VibyRoom.Api.RateLimiting;
using VibyRoom.Application;
using VibyRoom.Application.Abstractions.Authentication;
using VibyRoom.Infrastructure;
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
            ?? throw new ArgumentNullException(
                nameof(configuration));

        _environment = environment
            ?? throw new ArgumentNullException(
                nameof(environment));
    }

    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddControllers();

        services.AddOpenApi(options =>
        {
            options.AddOperationTransformer<
                AntiforgeryHeaderOpenApiTransformer>();
        });

        services.AddRoomApiErrorHandling();

        services.AddRoomRateLimiting(_configuration);

        services.AddHttpContextAccessor();

        services.AddScoped<
            ICurrentGuest,
            HttpContextCurrentGuest>();

        services.AddGuestDataProtection(
            _configuration,
            _environment);

        services.AddGuestAuthentication();
        services.AddGuestAntiforgery();

        services.AddApplication(_configuration);
        services.AddInfrastructure();

        var connectionString = _configuration
            .GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'DefaultConnection' is not configured.");
        }

        services.AddInfrastructurePersistance(
            connectionString);
    }

    public void Configure(WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseExceptionHandler();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();

            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint(
                    "/openapi/v1.json",
                    "VibyRoom API v1");
            });
        }

        app.UseHttpsRedirection();

        app.UseRouting();

        app.UseAuthentication();

        app.UseMiddleware<GuestAntiforgeryMiddleware>();

        app.UseAuthorization();

        app.UseRateLimiter();

        app.MapControllers();
    }
}