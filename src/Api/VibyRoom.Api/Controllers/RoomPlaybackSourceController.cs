using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VibyRoom.Api.Contracts.Music;
using VibyRoom.Api.Contracts.Playback;
using VibyRoom.Application.Music;

namespace VibyRoom.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/rooms/{roomId:guid}/playback/source")]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class RoomPlaybackSourceController : ControllerBase
{
    private readonly MusicStreamApplicationService _streamService;

    public RoomPlaybackSourceController(
        MusicStreamApplicationService streamService)
    {
        _streamService = streamService
            ?? throw new ArgumentNullException(
                nameof(streamService));
    }

    [HttpGet]
    [ProducesResponseType(
        typeof(PlaybackSourceResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PlaybackSourceResponse>> GetAsync(
        [FromRoute] Guid roomId,
        [FromQuery] Guid itemId,
        CancellationToken cancellationToken)
    {
        var source = await _streamService.GetCurrentStreamAsync(
            roomId,
            itemId,
            cancellationToken);

        var response = new PlaybackSourceResponse(
            new TrackReferenceResponse(
                source.Reference.Provider,
                source.Reference.TrackId),
            source.StreamUrl.AbsoluteUri,
            source.ExpiresAtUtc,
            source.ContentType);

        Response.Headers.CacheControl = "no-store";

        return Ok(response);
    }
}