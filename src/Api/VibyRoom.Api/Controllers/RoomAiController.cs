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
        "Uses GigaChat 2 Lite and Audius to find 15 unique playable " +
        "tracks, appends them to the room queue, saves the AI prompt " +
        "and resets the previous AI recommendation history. " +
        "Only the room owner may call this endpoint. " +
        "Requires the authenticated guest cookie and X-CSRF-TOKEN. " +
        "The operation is atomic and not idempotent.")]
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
        "Reuses the previous stored AI prompt and generates 15 " +
        "additional unique playable Audius tracks. Previously " +
        "selected tracks from the current AI playlist are excluded. " +
        "Requires room ownership, an authenticated guest cookie " +
        "and X-CSRF-TOKEN. The operation is atomic and not idempotent.")]
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

    private ActionResult<RoomMusicStateResponse> ToResponse(
        RoomMusicStateDetails state)
    {
        Response.Headers.ETag =
            $"\"q-{state.Version.ToString(CultureInfo.InvariantCulture)}\"";

        return Ok(
            RoomMusicStateResponseMapper.ToResponse(state));
    }
}