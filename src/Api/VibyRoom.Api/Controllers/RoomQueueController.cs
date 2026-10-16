using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VibyRoom.Api.Contracts.Queue;
using VibyRoom.Api.Mapping;
using VibyRoom.Application.Errors;
using VibyRoom.Application.Queue;
using VibyRoom.Domain.Music.Tracks;

namespace VibyRoom.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/rooms/{roomId:guid}/queue")]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class RoomQueueController : ControllerBase
{
    private readonly RoomQueueApplicationService _queueService;

    public RoomQueueController(
        RoomQueueApplicationService queueService)
    {
        _queueService = queueService
            ?? throw new ArgumentNullException(nameof(queueService));
    }

    [HttpGet]
    [ProducesResponseType(
        typeof(RoomQueueResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoomQueueResponse>> GetQueueAsync(
        [FromRoute] Guid roomId,
        CancellationToken cancellationToken)
    {
        var queue = await _queueService.GetQueueAsync(
            roomId,
            cancellationToken);

        SetETag(queue.Version);

        return Ok(RoomQueueResponseMapper.ToResponse(queue));
    }

    [HttpPost("items")]
    [ProducesResponseType(
        typeof(RoomQueueResponse),
        StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<RoomQueueResponse>> AddItemAsync(
        [FromRoute] Guid roomId,
        [FromBody] AddQueueItemRequest request,
        CancellationToken cancellationToken)
    {
        var reference = new TrackReference(
            request.Provider!.Trim(),
            request.TrackId!.Trim());

        var queue = await _queueService.AddTrackAsync(
            roomId,
            reference,
            cancellationToken);

        SetETag(queue.Version);

        var response = RoomQueueResponseMapper.ToResponse(queue);

        return CreatedAtAction(
            nameof(GetQueueAsync),
            new { roomId },
            response);
    }

    [HttpDelete("items/{itemId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> RemoveItemAsync(
        [FromRoute] Guid roomId,
        [FromRoute] Guid itemId,
        CancellationToken cancellationToken)
    {
        var expectedVersion = ReadExpectedVersion();

        var queue = await _queueService.RemoveTrackAsync(
            roomId,
            itemId,
            expectedVersion,
            cancellationToken);

        SetETag(queue.Version);

        return NoContent();
    }

    [HttpPatch("items/{itemId:guid}/position")]
    [ProducesResponseType(
        typeof(RoomQueueResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<RoomQueueResponse>> MoveItemAsync(
        [FromRoute] Guid roomId,
        [FromRoute] Guid itemId,
        [FromBody] MoveQueueItemRequest request,
        CancellationToken cancellationToken)
    {
        var expectedVersion = ReadExpectedVersion();

        var queue = await _queueService.MoveTrackAsync(
            roomId,
            itemId,
            request.Position!.Value,
            expectedVersion,
            cancellationToken);

        SetETag(queue.Version);

        return Ok(RoomQueueResponseMapper.ToResponse(queue));
    }

    private long ReadExpectedVersion()
    {
        if (!Request.Headers.TryGetValue(
                "If-Match",
                out var header) ||
            header.Count == 0)
        {
            throw new RoomApplicationException(
                ApplicationErrorCode.QueuePreconditionRequired,
                "If-Match is required.");
        }

        if (header.Count != 1)
        {
            throw InvalidETag();
        }

        var value = header[0];

        if (value is null ||
            !value.StartsWith("\"q-", StringComparison.Ordinal) ||
            !value.EndsWith('"'))
        {
            throw InvalidETag();
        }

        var versionPart = value[3..^1];

        if (!long.TryParse(
                versionPart,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var version))
        {
            throw InvalidETag();
        }

        return version;
    }

    private static RoomApplicationException InvalidETag()
    {
        return new RoomApplicationException(
            ApplicationErrorCode.Validation,
            "If-Match must contain a valid queue ETag.");
    }

    private void SetETag(long version)
    {
        Response.Headers.ETag =
            $"\"q-{version.ToString(CultureInfo.InvariantCulture)}\"";
    }
}