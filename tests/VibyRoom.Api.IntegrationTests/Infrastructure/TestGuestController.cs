using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VibyRoom.Application.Abstractions.Authentication;

namespace VibyRoom.Api.IntegrationTests.Infrastructure;

[ApiController]
[Route("api/v1/test/guest")]
public sealed class TestGuestController : ControllerBase
{
    private readonly ICurrentGuest _currentGuest;

    public TestGuestController(ICurrentGuest currentGuest)
    {
        _currentGuest = currentGuest
                        ?? throw new ArgumentNullException(nameof(currentGuest));
    }

    [HttpGet("identity")]
    [AllowAnonymous]
    public IActionResult GetIdentity()
    {
        return Ok(new
        {
            GuestId = _currentGuest.GuestId
        });
    }

    [HttpGet("protected")]
    [Authorize]
    public IActionResult GetProtected()
    {
        return Ok(new
        {
            GuestId = _currentGuest.GuestId
        });
    }

    [HttpPost("mutation")]
    [HttpPut("mutation")]
    [HttpPatch("mutation")]
    [HttpDelete("mutation")]
    [AllowAnonymous]
    public IActionResult Mutate()
    {
        return Ok(new
        {
            Accepted = true
        });
    }
}