using System.Globalization;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using VibyRoom.Application.Abstractions.Authentication;

namespace VibyRoom.Api.RateLimiting;

public static class RoomRateLimitingExtensions
{
    public static IServiceCollection AddRoomRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var settings = new RoomRateLimitOptions();

        configuration
            .GetSection(RoomRateLimitOptions.SectionName)
            .Bind(settings);

        ValidateSettings(settings);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode =
                StatusCodes.Status429TooManyRequests;

            options.AddPolicy(
                RoomRateLimitOptions.RoomCreationPolicy,
                context =>
                {
                    var guestId = context.RequestServices
                        .GetRequiredService<ICurrentGuest>()
                        .GuestId;

                    var partitionKey = guestId.HasValue
                        ? $"guest:{guestId.Value:D}"
                        : "guest:unauthenticated";

                    return RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey,
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit =
                                settings.RoomCreationPermitLimit,

                            Window = TimeSpan.FromSeconds(
                                settings.RoomCreationWindowSeconds),

                            QueueProcessingOrder =
                                QueueProcessingOrder.OldestFirst,

                            QueueLimit = 0,
                            AutoReplenishment = false
                        });
                });

            options.AddPolicy(
                RoomRateLimitOptions.InvitationAccessPolicy,
                context =>
                {
                    var remoteAddress = context.Connection
                        .RemoteIpAddress?
                        .ToString() ?? "unknown";

                    return RateLimitPartition.GetFixedWindowLimiter(
                        $"ip:{remoteAddress}",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit =
                                settings.InvitationAccessPermitLimit,

                            Window = TimeSpan.FromSeconds(
                                settings.InvitationAccessWindowSeconds),

                            QueueProcessingOrder =
                                QueueProcessingOrder.OldestFirst,

                            QueueLimit = 0,
                            AutoReplenishment = false
                        });
                });

            options.AddPolicy(
                RoomRateLimitOptions.MusicCatalogPolicy,
                _ => RateLimitPartition.GetFixedWindowLimiter(
                    "music:global",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit =
                            settings.MusicCatalogPermitLimit,

                        Window = TimeSpan.FromSeconds(
                            settings.MusicCatalogWindowSeconds),

                        QueueProcessingOrder =
                            QueueProcessingOrder.OldestFirst,

                        QueueLimit = 0,
                        AutoReplenishment = true
                    }));

            options.OnRejected = async (
                context,
                cancellationToken) =>
            {
                var httpContext = context.HttpContext;

                httpContext.Response.StatusCode =
                    StatusCodes.Status429TooManyRequests;

                httpContext.Response.Headers.CacheControl =
                    "no-store";

                if (context.Lease.TryGetMetadata(
                        MetadataName.RetryAfter,
                        out var retryAfter))
                {
                    var seconds = Math.Max(
                        1,
                        (int)Math.Ceiling(
                            retryAfter.TotalSeconds));

                    httpContext.Response.Headers.RetryAfter =
                        seconds.ToString(
                            CultureInfo.InvariantCulture);
                }

                var problem = new ProblemDetails
                {
                    Type = "urn:vibyroom:problem:ratelimitexceeded",
                    Title = "Too many requests",
                    Status = StatusCodes.Status429TooManyRequests,
                    Detail = "The request rate limit was exceeded."
                };

                problem.Extensions["code"] =
                    "RateLimitExceeded";

                problem.Extensions["traceId"] =
                    httpContext.TraceIdentifier;

                await httpContext.Response.WriteAsJsonAsync(
                    problem,
                    options: (JsonSerializerOptions?)null,
                    contentType: "application/problem+json",
                    cancellationToken: cancellationToken);
            };
            
            options.AddPolicy(
                RoomRateLimitOptions.AiGenerationPolicy,
                context =>
                {
                    var guestId = context.RequestServices
                        .GetRequiredService<ICurrentGuest>()
                        .GuestId;

                    var key = guestId.HasValue
                        ? $"ai:{guestId.Value:D}"
                        : "ai:unauthenticated";

                    return RateLimitPartition.GetFixedWindowLimiter(
                        key,
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = settings.AiGenerationPermitLimit,
                            Window = TimeSpan.FromSeconds(
                                settings.AiGenerationWindowSeconds),
                            QueueProcessingOrder =
                                QueueProcessingOrder.OldestFirst,
                            QueueLimit = 0,
                            AutoReplenishment = true
                        });
                });
        });

        return services;
    }

    private static void ValidateSettings(
        RoomRateLimitOptions settings)
    {
        if (settings.RoomCreationPermitLimit <= 0)
        {
            throw new InvalidOperationException(
                "RoomCreationPermitLimit must be positive.");
        }

        if (settings.RoomCreationWindowSeconds <= 0)
        {
            throw new InvalidOperationException(
                "RoomCreationWindowSeconds must be positive.");
        }

        if (settings.InvitationAccessPermitLimit <= 0)
        {
            throw new InvalidOperationException(
                "InvitationAccessPermitLimit must be positive.");
        }

        if (settings.InvitationAccessWindowSeconds <= 0)
        {
            throw new InvalidOperationException(
                "InvitationAccessWindowSeconds must be positive.");
        }

        if (settings.MusicCatalogPermitLimit <= 0)
        {
            throw new InvalidOperationException(
                "MusicCatalogPermitLimit must be positive.");
        }

        if (settings.MusicCatalogWindowSeconds <= 0)
        {
            throw new InvalidOperationException(
                "MusicCatalogWindowSeconds must be positive.");
        }
        
        if (settings.AiGenerationPermitLimit <= 0 ||
            settings.AiGenerationWindowSeconds <= 0)
        {
            throw new InvalidOperationException(
                "AI generation rate limit settings must be positive.");
        }
    }
}