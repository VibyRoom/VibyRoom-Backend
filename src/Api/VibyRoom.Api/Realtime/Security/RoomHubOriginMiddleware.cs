namespace VibyRoom.Api.Realtime.Security;

public sealed class RoomHubOriginMiddleware
{
    private readonly RequestDelegate _next;

    public RoomHubOriginMiddleware(RequestDelegate next)
    {
        _next = next
                ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task InvokeAsync(
        HttpContext context,
        RoomHubOriginPolicy originPolicy)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(originPolicy);

        if (!context.Request.Path.StartsWithSegments(
                "/hubs/rooms",
                StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var origins = context.Request.Headers.Origin;

        // Reject ambiguous or untrusted Origin values.
        if (origins.Count > 1 ||
            (origins.Count == 1 &&
             !originPolicy.IsAllowed(origins[0])))
        {
            context.Response.StatusCode =
                StatusCodes.Status403Forbidden;

            return;
        }

        // Browser WebSocket requests must have an Origin.
        // Non-browser clients without Origin are denied as well.
        if (context.WebSockets.IsWebSocketRequest &&
            origins.Count == 0)
        {
            context.Response.StatusCode =
                StatusCodes.Status403Forbidden;

            return;
        }

        await _next(context);
    }
}