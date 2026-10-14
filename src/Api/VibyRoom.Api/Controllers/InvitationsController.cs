using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VibyRoom.Api.Contracts.Rooms;
using VibyRoom.Api.Mapping;
using VibyRoom.Application.Contracts.Rooms;
using VibyRoom.Application.Rooms;

namespace VibyRoom.Api.Controllers;

[ApiController]
[Route("api/v1/invitations")]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class InvitationsController : ControllerBase
{
    private readonly RoomApplicationService _roomApplicationService;

    public InvitationsController(
        RoomApplicationService roomApplicationService)
    {
        _roomApplicationService = roomApplicationService
            ?? throw new ArgumentNullException(
                nameof(roomApplicationService));
    }

    [HttpGet("{code}")]
    [AllowAnonymous]
    [EnableRateLimiting("InvitationAccess")]
    [EndpointSummary("Preview a room invitation")]
    [EndpointDescription(
        "Returns public room information for a valid invitation. " +
        "Authentication and CSRF tokens are not required. " +
        "Room members and invitation details are not disclosed.")]
    [ProducesResponseType(
        typeof(RoomPreviewResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<RoomPreviewResponse>>
        GetInvitationPreviewAsync(
            [FromRoute] string code,
            CancellationToken cancellationToken)
    {
        var preview = await _roomApplicationService
            .GetInvitationPreviewAsync(
                code,
                cancellationToken);

        return Ok(RoomResponseMapper.ToResponse(preview));
    }

    [HttpPost("{code}/join")]
    [Authorize]
    [EnableRateLimiting("InvitationAccess")]
    [EndpointSummary("Join a music room")]
    [EndpointDescription(
        "Joins a room using an invitation code. " +
        "Returns 201 when membership is created, or 200 " +
        "when the guest is already a member. " +
        "Requires a guest authentication cookie " +
        "and X-CSRF-TOKEN header.")]
    [ProducesResponseType(
        typeof(RoomDetailsResponse),
        StatusCodes.Status201Created)]
    [ProducesResponseType(
        typeof(RoomDetailsResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<RoomDetailsResponse>> JoinRoomAsync(
        [FromRoute] string code,
        [FromBody] JoinRoomRequest request,
        CancellationToken cancellationToken)
    {
        var command = new JoinRoomCommand(
            code,
            request.DisplayName!.Trim());

        var result = await _roomApplicationService.JoinRoomAsync(
            command,
            cancellationToken);

        var response = RoomResponseMapper.ToResponse(result.Room);

        if (!result.Joined)
        {
            return Ok(response);
        }

        return CreatedAtRoute(
            RoomsController.GetRoomRouteName,
            new { roomId = response.Id },
            response);
    }
}