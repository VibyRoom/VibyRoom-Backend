using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using VibyRoom.Application.Errors;

namespace VibyRoom.Api.Errors;

public sealed class RoomApiExceptionHandler : IExceptionHandler
{
    private readonly ILogger<RoomApiExceptionHandler> _logger;

    public RoomApiExceptionHandler(
        ILogger<RoomApiExceptionHandler> logger)
    {
        _logger = logger
            ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is OperationCanceledException)
        {
            return false;
        }

        ApplicationErrorCode? applicationErrorCode = exception switch
        {
            RoomApplicationException roomException =>
                roomException.ErrorCode,

            MusicCatalogApplicationException musicException =>
                musicException.ErrorCode,

            _ => null
        };

        ProblemDetails problem;
        string errorCode;

        if (applicationErrorCode is { } code)
        {
            errorCode = code.ToString();
            problem = CreateApplicationProblem(code);
        }
        else
        {
            errorCode = "InternalServerError";

            problem = new ProblemDetails
            {
                Type = "urn:vibyroom:problem:internalservererror",
                Title = "Internal server error",
                Status = StatusCodes.Status500InternalServerError,
                Detail = "An unexpected server error occurred."
            };

            _logger.LogError(
                exception,
                "Unhandled API exception. TraceId: {TraceId}",
                httpContext.TraceIdentifier);
        }

        problem.Extensions["code"] = errorCode;
        problem.Extensions["traceId"] =
            httpContext.TraceIdentifier;

        httpContext.Response.StatusCode =
            problem.Status ?? StatusCodes.Status500InternalServerError;

        httpContext.Response.Headers.CacheControl = "no-store";

        await httpContext.Response.WriteAsJsonAsync(
            problem,
            options: (JsonSerializerOptions?)null,
            contentType: "application/problem+json",
            cancellationToken: cancellationToken);

        return true;
    }

    private static ProblemDetails CreateApplicationProblem(
        ApplicationErrorCode code)
    {
        var (status, title, detail) = code switch
        {
            ApplicationErrorCode.GuestSessionRequired =>
                (
                    StatusCodes.Status401Unauthorized,
                    "Guest session required",
                    "An active guest session is required."
                ),

            ApplicationErrorCode.Validation =>
                (
                    StatusCodes.Status400BadRequest,
                    "Invalid request",
                    "The request contains invalid values."
                ),

            ApplicationErrorCode.RoomNotFound or
            ApplicationErrorCode.NotMember =>
                (
                    StatusCodes.Status404NotFound,
                    "Room not found",
                    "The requested room is unavailable."
                ),

            ApplicationErrorCode.InvalidInvitation =>
                (
                    StatusCodes.Status404NotFound,
                    "Invitation not found",
                    "The invitation is invalid or unavailable."
                ),

            ApplicationErrorCode.MemberNotFound =>
                (
                    StatusCodes.Status404NotFound,
                    "Member not found",
                    "The requested room member is unavailable."
                ),

            ApplicationErrorCode.AccessDenied =>
                (
                    StatusCodes.Status403Forbidden,
                    "Access denied",
                    "You do not have permission to perform this action."
                ),

            ApplicationErrorCode.RoomFull =>
                (
                    StatusCodes.Status409Conflict,
                    "Room is full",
                    "The room has reached its member capacity."
                ),

            ApplicationErrorCode.OwnerTransferRequired =>
                (
                    StatusCodes.Status409Conflict,
                    "Ownership transfer required",
                    "Transfer ownership before leaving the room."
                ),

            ApplicationErrorCode.RoomClosed =>
                (
                    StatusCodes.Status409Conflict,
                    "Room is closed",
                    "The room is no longer active."
                ),

            ApplicationErrorCode.ConcurrencyConflict =>
                (
                    StatusCodes.Status409Conflict,
                    "Concurrency conflict",
                    "The resource was modified concurrently. Reload and retry."
                ),

            ApplicationErrorCode.InviteCodeConflict =>
                (
                    StatusCodes.Status409Conflict,
                    "Invitation conflict",
                    "The invitation could not be created due to a conflict."
                ),

            ApplicationErrorCode.TrackNotFound =>
                (
                    StatusCodes.Status404NotFound,
                    "Track not found",
                    "The requested music track is unavailable."
                ),

            ApplicationErrorCode.UnsupportedMusicProvider =>
                (
                    StatusCodes.Status400BadRequest,
                    "Unsupported music provider",
                    "The specified music provider is not supported."
                ),

            ApplicationErrorCode.MusicProviderUnavailable =>
                (
                    StatusCodes.Status503ServiceUnavailable,
                    "Music provider unavailable",
                    "The music catalog is temporarily unavailable."
                ),

            ApplicationErrorCode.MusicProviderRateLimited =>
                (
                    StatusCodes.Status503ServiceUnavailable,
                    "Music provider rate limited",
                    "The music catalog request limit has been reached."
                ),

            ApplicationErrorCode.QueueFull =>
                (
                    StatusCodes.Status409Conflict,
                    "Queue is full",
                    "The music queue has reached its maximum size."
                ),

            ApplicationErrorCode.QueueItemNotFound =>
                (
                    StatusCodes.Status404NotFound,
                    "Queue item not found",
                    "The requested queue item is unavailable."
                ),

            ApplicationErrorCode.InvalidQueuePosition =>
                (
                    StatusCodes.Status400BadRequest,
                    "Invalid queue position",
                    "The requested position is outside the queue."
                ),

            ApplicationErrorCode.TrackNotEligible =>
                (
                    StatusCodes.Status400BadRequest,
                    "Track not eligible",
                    "This track cannot be added to the music queue."
                ),

            ApplicationErrorCode.QueueVersionMismatch =>
                (
                    StatusCodes.Status412PreconditionFailed,
                    "Queue version mismatch",
                    "The queue has changed. Reload and retry."
                ),

            ApplicationErrorCode.QueuePreconditionRequired =>
                (
                    StatusCodes.Status428PreconditionRequired,
                    "Queue precondition required",
                    "A valid If-Match header is required."
                ),

            _ => throw new ArgumentOutOfRangeException(
                nameof(code),
                code,
                "Unsupported application error code.")
        };

        return new ProblemDetails
        {
            Type =
                $"urn:vibyroom:problem:{code.ToString().ToLowerInvariant()}",
            Title = title,
            Status = status,
            Detail = detail
        };
    }
}