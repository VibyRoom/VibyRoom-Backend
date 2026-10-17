using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VibyRoom.Api.Contracts.Ai;
using VibyRoom.Api.Contracts.Playback;
using VibyRoom.Api.Mapping;
using VibyRoom.Api.RateLimiting;
using VibyRoom.Application.Contracts.Playback;
using VibyRoom.Application.Music;

namespace VibyRoom.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/rooms/{roomId:guid}/ai")]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status403Forbidden)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public sealed class RoomAiController : ControllerBase
{
    private readonly AiPlaylistApplicationService _service;

    public RoomAiController(
        AiPlaylistApplicationService service)
    {
        _service = service
            ?? throw new ArgumentNullException(nameof(service));
    }

    [HttpGet]
    [EndpointSummary("Get current AI playlist context")]
    [EndpointDescription(
        "Returns the stored AI prompt to the room owner. " +
        "The prompt is preserved between requests and server restarts.")]
    [ProducesResponseType(
        typeof(AiSessionResponse),
        StatusCodes.Status200OK)]
    public async Task<ActionResult<AiSessionResponse>> GetAsync(
        [FromRoute] Guid roomId,
        CancellationToken cancellationToken)
    {
        var prompt = await _service.GetPromptAsync(
            roomId,
            cancellationToken);

        return Ok(new AiSessionResponse(
            prompt,
            !string.IsNullOrWhiteSpace(prompt)));
    }

    [HttpPost("generate")]
    [EnableRateLimiting(RoomRateLimitOptions.AiGenerationPolicy)]
    [EndpointSummary("Generate 15 AI tracks")]
    [EndpointDescription(
        "Uses GigaChat and Audius to find 15 unique playable tracks, " +
        "appends them to the room queue and replaces the AI context. " +
        "Only the owner may call this endpoint; X-CSRF-TOKEN is required.")]
    [ProducesResponseType(
        typeof(RoomMusicStateResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<RoomMusicStateResponse>> GenerateAsync(
        [FromRoute] Guid roomId,
        [FromBody] GenerateAiPlaylistRequest request,
        CancellationToken cancellationToken)
    {
        var state = await _service.GenerateAsync(
            roomId,
            request.Prompt!,
            cancellationToken);

        return ToResponse(state);
    }

    [HttpPost("more")]
    [EnableRateLimiting(RoomRateLimitOptions.AiGenerationPolicy)]
    [EndpointSummary("Add 15 more AI tracks")]
    [EndpointDescription(
        "Uses the stored AI context to append 15 new playable tracks. " +
        "Requires room ownership and X-CSRF-TOKEN.")]
    [ProducesResponseType(
        typeof(RoomMusicStateResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<RoomMusicStateResponse>> MoreAsync(
        [FromRoute] Guid roomId,
        CancellationToken cancellationToken)
    {
        var state = await _service.MoreAsync(
            roomId,
            cancellationToken);

        return ToResponse(state);
    }

    [HttpPost("random")]
    [EnableRateLimiting(RoomRateLimitOptions.AiGenerationPolicy)]
    [EndpointSummary("Add 15 surprise tracks")]
    [EndpointDescription(
        "Randomly chooses genres and mood, then uses GigaChat and Audius " +
        "to append 15 unique playable tracks. Works with an empty queue. " +
        "Saves the generated theme so More can continue it. " +
        "Requires room ownership and X-CSRF-TOKEN.")]
    [ProducesResponseType(
        typeof(RoomMusicStateResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<RoomMusicStateResponse>> RandomAsync(
        [FromRoute] Guid roomId,
        CancellationToken cancellationToken)
    {
        var state = await _service.RandomAsync(
            roomId,
            cancellationToken);

        return ToResponse(state);
    }

    [HttpPost("similar")]
    [EnableRateLimiting(RoomRateLimitOptions.AiGenerationPolicy)]
    [EndpointSummary(
        "Add 15 tracks similar to the current room music")]
    [EndpointDescription(
        "Builds a prompt from the current playing track and pending queue, " +
        "then appends 15 unique playable Audius tracks. " +
        "Returns 409 if the room has no reference tracks. " +
        "Requires room ownership and X-CSRF-TOKEN.")]
    [ProducesResponseType(
        typeof(RoomMusicStateResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<RoomMusicStateResponse>> SimilarAsync(
        [FromRoute] Guid roomId,
        CancellationToken cancellationToken)
    {
        var state = await _service.SimilarAsync(
            roomId,
            cancellationToken);

        return ToResponse(state);
    }

    private ActionResult<RoomMusicStateResponse> ToResponse(
        RoomMusicStateDetails state)
    {
        Response.Headers.ETag =
            $"\"q-{state.Version.ToString(CultureInfo.InvariantCulture)}\"";

        return Ok(
            RoomMusicStateResponseMapper.ToResponse(state));
    }
}