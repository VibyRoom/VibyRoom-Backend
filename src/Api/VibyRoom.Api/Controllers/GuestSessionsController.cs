using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using VibyRoom.Api.Authentication;
using VibyRoom.Api.Contracts;
using VibyRoom.Application.Abstractions.Authentication;

namespace VibyRoom.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/v1/guest-sessions")]
public sealed class GuestSessionsController : ControllerBase
{
    private readonly ICurrentGuest _currentGuest;
    private readonly IOptionsMonitor<CookieAuthenticationOptions> _cookieOptions;

    public GuestSessionsController(
        ICurrentGuest currentGuest,
        IOptionsMonitor<CookieAuthenticationOptions> cookieOptions)
    {
        _currentGuest = currentGuest
            ?? throw new ArgumentNullException(nameof(currentGuest));

        _cookieOptions = cookieOptions
            ?? throw new ArgumentNullException(nameof(cookieOptions));
    }

    [HttpPost]
    [ProducesResponseType(
        typeof(GuestSessionResponse),
        StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<GuestSessionResponse>> CreateOrContinueAsync()
    {
        var authenticationResult = await HttpContext.AuthenticateAsync(
            GuestAuthenticationDefaults.AuthenticationScheme);

        var currentGuestId = _currentGuest.GuestId;

        if (authenticationResult.Succeeded &&
            currentGuestId.HasValue &&
            authenticationResult.Ticket?.Properties.ExpiresUtc
                is { } existingExpiresAtUtc)
        {
            return Ok(new GuestSessionResponse(
                currentGuestId.Value,
                existingExpiresAtUtc));
        }

        var cookieOptions = _cookieOptions.Get(
            GuestAuthenticationDefaults.AuthenticationScheme);

        var issuedAtUtc = (cookieOptions.TimeProvider
                           ?? TimeProvider.System).GetUtcNow();

        var expiresAtUtc = issuedAtUtc.Add(
            GuestAuthenticationDefaults.SessionLifetime);

        var guestId = Guid.NewGuid();

        var identity = new ClaimsIdentity(
            [
                new Claim(
                    GuestAuthenticationDefaults.GuestIdClaimType,
                    guestId.ToString("D"))
            ],
            GuestAuthenticationDefaults.AuthenticationScheme);

        var principal = new ClaimsPrincipal(identity);

        var authenticationProperties = new AuthenticationProperties
        {
            IsPersistent = true,
            IssuedUtc = issuedAtUtc,
            ExpiresUtc = expiresAtUtc
        };

        await HttpContext.SignInAsync(
            GuestAuthenticationDefaults.AuthenticationScheme,
            principal,
            authenticationProperties);

        return Ok(new GuestSessionResponse(
            guestId,
            expiresAtUtc));
    }
}