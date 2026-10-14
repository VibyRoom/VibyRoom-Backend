using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using VibyRoom.Api.Contracts.Rooms;
using VibyRoom.Api.Mapping;
using VibyRoom.Application.Contracts.Rooms;
using VibyRoom.Application.Rooms;

namespace VibyRoom.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/rooms")]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class RoomsController : ControllerBase
{
    public const string GetRoomRouteName = "Rooms.Get";

    private readonly RoomApplicationService _roomApplicationService;

    public RoomsController(
        RoomApplicationService roomApplicationService)
    {
        _roomApplicationService = roomApplicationService
            ?? throw new ArgumentNullException(
                nameof(roomApplicationService));
    }

    [HttpPost]
    [EnableRateLimiting("RoomCreation")]
    [EndpointSummary("Create a music room")]
    [EndpointDescription(
        "Creates a room for the authenticated guest. " +
        "Requires the X-CSRF-TOKEN header.")]
    [ProducesResponseType(
        typeof(RoomDetailsResponse),
        StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<RoomDetailsResponse>> CreateRoomAsync(
        [FromBody] CreateRoomRequest request,
        CancellationToken cancellationToken)
    {
        var command = new CreateRoomCommand(
            request.Name!.Trim(),
            request.DisplayName!.Trim());

        var room = await _roomApplicationService.CreateRoomAsync(
            command,
            cancellationToken);

        var response = RoomResponseMapper.ToResponse(room);

        return CreatedAtRoute(
            GetRoomRouteName,
            new { roomId = response.Id },
            response);
    }

    [HttpGet("{roomId}", Name = GetRoomRouteName)]
    [EndpointSummary("Get room details")]
    [EndpointDescription(
        "Returns room details to an authenticated room member. " +
        "Non-members and closed rooms receive 404.")]
    [ProducesResponseType(
        typeof(RoomDetailsResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RoomDetailsResponse>> GetRoomAsync(
        [FromRoute] Guid roomId,
        CancellationToken cancellationToken)
    {
        var room = await _roomApplicationService.GetRoomAsync(
            roomId,
            cancellationToken);

        return Ok(RoomResponseMapper.ToResponse(room));
    }

    [HttpDelete("{roomId}/members/me")]
    [EndpointSummary("Leave a music room")]
    [EndpointDescription(
        "Removes the authenticated guest from the room. " +
        "The owner must transfer ownership before leaving " +
        "if other members are present. Requires X-CSRF-TOKEN.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> LeaveRoomAsync(
        [FromRoute] Guid roomId,
        CancellationToken cancellationToken)
    {
        await _roomApplicationService.LeaveRoomAsync(
            roomId,
            cancellationToken);

        return NoContent();
    }

    [HttpDelete("{roomId}/members/{guestId}")]
    [EndpointSummary("Remove a room member")]
    [EndpointDescription(
        "Removes the specified member. Only the room owner " +
        "may perform this operation. The guestId route value " +
        "identifies the target member. Requires X-CSRF-TOKEN.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveMemberAsync(
        [FromRoute] Guid roomId,
        [FromRoute] Guid guestId,
        CancellationToken cancellationToken)
    {
        await _roomApplicationService.RemoveMemberAsync(
            roomId,
            guestId,
            cancellationToken);

        return NoContent();
    }

    [HttpPut("{roomId}/owner")]
    [EndpointSummary("Transfer room ownership")]
    [EndpointDescription(
        "Transfers ownership to an existing room member. " +
        "Only the current owner may perform this operation. " +
        "Requires X-CSRF-TOKEN.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> TransferOwnershipAsync(
        [FromRoute] Guid roomId,
        [FromBody] TransferOwnershipRequest request,
        CancellationToken cancellationToken)
    {
        await _roomApplicationService.TransferOwnershipAsync(
            roomId,
            request.NewOwnerGuestId,
            cancellationToken);

        return NoContent();
    }

    [HttpPost("{roomId}/close")]
    [EndpointSummary("Close a music room")]
    [EndpointDescription(
        "Closes the room permanently. Only the owner " +
        "may perform this operation. Requires X-CSRF-TOKEN.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CloseRoomAsync(
        [FromRoute] Guid roomId,
        CancellationToken cancellationToken)
    {
        await _roomApplicationService.CloseRoomAsync(
            roomId,
            cancellationToken);

        return NoContent();
    }
}