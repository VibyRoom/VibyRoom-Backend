using Microsoft.AspNetCore.Mvc;

namespace VibyRoom.Api.Errors;

public static class RoomApiErrorHandlingExtensions
{
    public static IServiceCollection AddRoomApiErrorHandling(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProblemDetails();

        services.AddExceptionHandler<RoomApiExceptionHandler>();

        services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var problem = new ValidationProblemDetails(
                    context.ModelState)
                {
                    Type = "urn:vibyroom:problem:validation",
                    Title = "Request validation failed",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = "One or more request parameters are invalid."
                };

                problem.Extensions["code"] = "Validation";
                problem.Extensions["traceId"] =
                    context.HttpContext.TraceIdentifier;

                context.HttpContext.Response.Headers.CacheControl =
                    "no-store";

                return new BadRequestObjectResult(problem)
                {
                    ContentTypes =
                    {
                        "application/problem+json"
                    }
                };
            };
        });

        return services;
    }
}