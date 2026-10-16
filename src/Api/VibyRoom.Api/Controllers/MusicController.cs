using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VibyRoom.Api.Contracts.Music;
using VibyRoom.Api.Mapping;
using VibyRoom.Api.RateLimiting;
using VibyRoom.Application.Errors;
using VibyRoom.Application.Music;
using VibyRoom.Domain.Music.Tracks;

namespace VibyRoom.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/music")]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class MusicController : ControllerBase
{
    private readonly MusicCatalogService _musicCatalogService;

    public MusicController(
        MusicCatalogService musicCatalogService)
    {
        _musicCatalogService = musicCatalogService
            ?? throw new ArgumentNullException(
                nameof(musicCatalogService));
    }

    [HttpGet("tracks/search")]
    [EnableRateLimiting(RoomRateLimitOptions.MusicCatalogPolicy)]
    [EndpointSummary("Search music tracks")]
    [EndpointDescription(
        "Searches the Audius music catalog by title or artist. " +
        "Requires an authenticated VibyRoom guest session. " +
        "The query must contain 2-100 characters after trimming. " +
        "The result limit defaults to 20 and must be 1-50.")]
    [ProducesResponseType(
        typeof(TrackSearchResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<TrackSearchResponse>>
        SearchTracksAsync(
            [FromQuery] TrackSearchQuery request,
            CancellationToken cancellationToken)
    {
        var tracks = await _musicCatalogService
            .SearchTracksAsync(
                request.Query!,
                request.Limit,
                cancellationToken);

        return Ok(
            MusicTrackResponseMapper.ToSearchResponse(tracks));
    }

    [HttpGet("tracks/details")]
    [EnableRateLimiting(RoomRateLimitOptions.MusicCatalogPolicy)]
    [EndpointSummary("Get music track details")]
    [EndpointDescription(
        "Returns metadata for a track identified by provider " +
        "and external track ID. Requires an authenticated " +
        "VibyRoom guest session. " +
        "The currently supported provider is audius.")]
    [ProducesResponseType(
        typeof(MusicTrackResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<MusicTrackResponse>>
        GetTrackDetailsAsync(
            [FromQuery] TrackDetailsQuery request,
            CancellationToken cancellationToken)
    {
        var reference = new TrackReference(
            request.Provider!,
            request.TrackId!);

        var track = await _musicCatalogService.GetTrackAsync(
            reference,
            cancellationToken);

        if (track is null)
        {
            throw new MusicCatalogApplicationException(
                ApplicationErrorCode.TrackNotFound,
                "The requested music track was not found.");
        }

        return Ok(
            MusicTrackResponseMapper.ToResponse(track));
    }
}