using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VibyRoom.Api.Authentication;
using VibyRoom.Api.Contracts;

namespace VibyRoom.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/security")]
public sealed class SecurityController : ControllerBase
{
    private readonly IAntiforgery _antiforgery;

    public SecurityController(IAntiforgery antiforgery)
    {
        _antiforgery = antiforgery
                       ?? throw new ArgumentNullException(nameof(antiforgery));
    }

    [HttpGet("antiforgery")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(
        typeof(AntiforgeryTokenResponse),
        StatusCodes.Status200OK)]
    public ActionResult<AntiforgeryTokenResponse> GetAntiforgeryToken()
    {
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);

        var requestToken = tokens.RequestToken
                           ?? throw new InvalidOperationException(
                               "Failed to generate antiforgery request token.");

        var response = new AntiforgeryTokenResponse(
            requestToken,
            GuestAntiforgeryExtensions.HeaderName);

        return Ok(response);
    }
}