using Microsoft.AspNetCore.Antiforgery;

namespace VibyRoom.Api.Authentication;

public sealed class GuestAntiforgeryMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IAntiforgery _antiforgery;

    public GuestAntiforgeryMiddleware(
        RequestDelegate next,
        IAntiforgery antiforgery)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _antiforgery = antiforgery
                       ?? throw new ArgumentNullException(nameof(antiforgery));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!RequiresAntiforgeryValidation(context.Request))
        {
            await _next(context);
            return;
        }

        try
        {
            await _antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            context.Response.StatusCode =
                StatusCodes.Status400BadRequest;

            return;
        }

        await _next(context);
    }

    private static bool RequiresAntiforgeryValidation(
        HttpRequest request)
    {
        if (!request.Path.StartsWithSegments(
                "/api",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return HttpMethods.IsPost(request.Method)
               || HttpMethods.IsPut(request.Method)
               || HttpMethods.IsPatch(request.Method)
               || HttpMethods.IsDelete(request.Method);
    }
}