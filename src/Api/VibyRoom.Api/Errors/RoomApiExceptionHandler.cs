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

        ProblemDetails problem;
        string errorCode;

        if (exception is RoomApplicationException applicationException)
        {
            errorCode = applicationException.ErrorCode.ToString();

            problem = CreateApplicationProblem(
                applicationException.ErrorCode);
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
                "Unhandled API exception of type {ExceptionType}. TraceId: {TraceId}",
                exception.GetType().FullName,
                httpContext.TraceIdentifier);
        }

        problem.Extensions["code"] = errorCode;
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

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
                    "The room was modified concurrently. Reload and retry."
                ),

            ApplicationErrorCode.InviteCodeConflict =>
                (
                    StatusCodes.Status409Conflict,
                    "Invitation conflict",
                    "The invitation could not be created due to a conflict."
                ),

            _ => throw new ArgumentOutOfRangeException(
                nameof(code),
                code,
                "Unsupported application error code.")
        };

        return new ProblemDetails
        {
            Type = $"urn:vibyroom:problem:{code.ToString().ToLowerInvariant()}",
            Title = title,
            Status = status,
            Detail = detail
        };
    }
}