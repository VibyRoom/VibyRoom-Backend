using VibyRoom.Api.Authentication;
using VibyRoom.Api.Background;
using VibyRoom.Api.Errors;
using VibyRoom.Api.OpenApi;
using VibyRoom.Api.RateLimiting;
using VibyRoom.Api.Realtime;
using VibyRoom.Api.Realtime.Security;
using VibyRoom.Application;
using VibyRoom.Application.Abstractions.Authentication;
using VibyRoom.Application.Abstractions.Realtime;
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
            ?? throw new ArgumentNullException(nameof(configuration));

        _environment = environment
            ?? throw new ArgumentNullException(nameof(environment));
    }

    public void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddControllers();

        services.AddOpenApi(options =>
        {
            options.AddOperationTransformer<
                AntiforgeryHeaderOpenApiTransformer>();

            options.AddOperationTransformer<
                IfMatchHeaderOpenApiTransformer>();
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
        services.AddInfrastructure(_configuration);

        var connectionString = _configuration
            .GetConnectionString("DefaultConnection");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'DefaultConnection' is not configured.");
        }

        services.AddInfrastructurePersistance(
            connectionString);

        services
            .AddOptions<PlaybackProgressionOptions>()
            .Bind(_configuration.GetSection(
                PlaybackProgressionOptions.SectionName))
            .Validate(
                options =>
                    options.IntervalMilliseconds is >= 100 and <= 60000,
                "PlaybackProgression:IntervalMilliseconds must be between 100 and 60000.")
            .Validate(
                options =>
                    options.BatchSize is >= 1 and <= 500,
                "PlaybackProgression:BatchSize must be between 1 and 500.")
            .ValidateOnStart();

        services.AddHostedService<PlaybackProgressionWorker>();

        services.AddSignalR();

        services.AddSingleton<RoomPresenceRegistry>();
        services.AddSingleton<RoomPresenceCoordinator>();

        services.AddSingleton<IRoomPresenceNotifier>(
            serviceProvider => serviceProvider
                .GetRequiredService<RoomPresenceCoordinator>());

        services.AddSingleton<
            IRoomRealtimePublisher,
            SignalRRoomRealtimePublisher>();

        var originPolicy = new RoomHubOriginPolicy(
            _configuration);

        services.AddSingleton(originPolicy);

        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy
                    .WithOrigins(originPolicy.AllowedOrigins.ToArray())
                    .WithMethods(
                        "GET",
                        "POST",
                        "PUT",
                        "PATCH",
                        "DELETE")
                    .AllowAnyHeader()
                    .AllowCredentials();
            });

            options.AddPolicy(
                RoomHubOriginPolicy.CorsPolicyName,
                policy =>
                {
                    policy
                        .WithOrigins(originPolicy.AllowedOrigins.ToArray())
                        .WithMethods(
                            "GET",
                            "POST",
                            "DELETE")
                        .AllowAnyHeader()
                        .AllowCredentials();
                });
        });
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

        var originPolicy = app.Services
            .GetRequiredService<RoomHubOriginPolicy>();

        var webSocketOptions = new WebSocketOptions();

        foreach (var origin in originPolicy.AllowedOrigins)
        {
            webSocketOptions.AllowedOrigins.Add(origin);
        }

        app.UseWebSockets(webSocketOptions);
        app.UseMiddleware<RoomHubOriginMiddleware>();

        app.UseCors();

        app.UseAuthentication();
        app.UseMiddleware<GuestAntiforgeryMiddleware>();
        app.UseAuthorization();
        app.UseRateLimiter();

        app.MapControllers();

        app.MapHub<RoomHub>("/hubs/rooms")
            .RequireCors(RoomHubOriginPolicy.CorsPolicyName);
    }
}