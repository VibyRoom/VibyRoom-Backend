using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VibyRoom.Api.Contracts.Playback;
using VibyRoom.Api.Mapping;
using VibyRoom.Application.Contracts.Playback;
using VibyRoom.Application.Errors;
using VibyRoom.Application.Playback;

namespace VibyRoom.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/rooms/{roomId:guid}")]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public sealed class RoomPlaybackController : ControllerBase
{
    private readonly RoomPlaybackApplicationService _playbackService;

    public RoomPlaybackController(
        RoomPlaybackApplicationService playbackService)
    {
        _playbackService = playbackService
            ?? throw new ArgumentNullException(
                nameof(playbackService));
    }

    [HttpGet("playback")]
    [ProducesResponseType(
        typeof(PlaybackResponse),
        StatusCodes.Status200OK)]
    public async Task<ActionResult<PlaybackResponse>> GetPlaybackAsync(
        [FromRoute] Guid roomId,
        CancellationToken cancellationToken)
    {
        var playback = await _playbackService.GetPlaybackAsync(
            roomId,
            cancellationToken);

        SetETag(playback.Version);

        return Ok(
            RoomMusicStateResponseMapper.ToPlaybackResponse(
                playback));
    }

    [HttpGet("music-state")]
    [ProducesResponseType(
        typeof(RoomMusicStateResponse),
        StatusCodes.Status200OK)]
    public async Task<ActionResult<RoomMusicStateResponse>> GetMusicStateAsync(
        [FromRoute] Guid roomId,
        CancellationToken cancellationToken)
    {
        var state = await _playbackService.GetMusicStateAsync(
            roomId,
            cancellationToken);

        return OkMusicState(state);
    }

    [HttpPost("playback/play")]
    [ProducesResponseType(
        typeof(RoomMusicStateResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<RoomMusicStateResponse>> PlayAsync(
        [FromRoute] Guid roomId,
        CancellationToken cancellationToken)
    {
        var expectedVersion = ReadExpectedVersion();

        var state = await _playbackService.PlayAsync(
            roomId,
            expectedVersion,
            cancellationToken);

        return OkMusicState(state);
    }

    [HttpPost("playback/pause")]
    [ProducesResponseType(
        typeof(RoomMusicStateResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<RoomMusicStateResponse>> PauseAsync(
        [FromRoute] Guid roomId,
        CancellationToken cancellationToken)
    {
        var expectedVersion = ReadExpectedVersion();

        var state = await _playbackService.PauseAsync(
            roomId,
            expectedVersion,
            cancellationToken);

        return OkMusicState(state);
    }

    [HttpPost("playback/resume")]
    [ProducesResponseType(
        typeof(RoomMusicStateResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<RoomMusicStateResponse>> ResumeAsync(
        [FromRoute] Guid roomId,
        CancellationToken cancellationToken)
    {
        var expectedVersion = ReadExpectedVersion();

        var state = await _playbackService.ResumeAsync(
            roomId,
            expectedVersion,
            cancellationToken);

        return OkMusicState(state);
    }

    [HttpPut("playback/position")]
    [ProducesResponseType(
        typeof(RoomMusicStateResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<RoomMusicStateResponse>> SeekAsync(
        [FromRoute] Guid roomId,
        [FromBody] SeekRequest request,
        CancellationToken cancellationToken)
    {
        var expectedVersion = ReadExpectedVersion();

        var state = await _playbackService.SeekAsync(
            roomId,
            request.PositionMs!.Value,
            expectedVersion,
            cancellationToken);

        return OkMusicState(state);
    }

    [HttpPost("playback/skip")]
    [ProducesResponseType(
        typeof(RoomMusicStateResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<RoomMusicStateResponse>> SkipAsync(
        [FromRoute] Guid roomId,
        CancellationToken cancellationToken)
    {
        var expectedVersion = ReadExpectedVersion();

        var state = await _playbackService.SkipAsync(
            roomId,
            expectedVersion,
            cancellationToken);

        return OkMusicState(state);
    }

    private ActionResult<RoomMusicStateResponse> OkMusicState(
        RoomMusicStateDetails state)
    {
        ArgumentNullException.ThrowIfNull(state);

        SetETag(state.Version);

        return Ok(
            RoomMusicStateResponseMapper.ToResponse(state));
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
            !value.StartsWith(
                "\"q-",
                StringComparison.Ordinal) ||
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
            "If-Match must contain a valid music state ETag.");
    }

    private void SetETag(long version)
    {
        Response.Headers.ETag =
            $"\"q-{version.ToString(CultureInfo.InvariantCulture)}\"";
    }
}