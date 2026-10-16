using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using VibyRoom.Application.Abstractions.Authentication;

namespace VibyRoom.Api.Authentication;

public sealed class HttpContextCurrentGuest : ICurrentGuest
{
    private const string GuestAuthenticationScheme = "Cookie";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentGuest(
        IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor
                               ?? throw new ArgumentNullException(
                                   nameof(httpContextAccessor));
    }

    public Guid? GuestId
    {
        get
        {
            var authenticationResult = _httpContextAccessor
                .HttpContext?
                .Features
                .Get<IAuthenticateResultFeature>()?
                .AuthenticateResult;

            if (authenticationResult is not { Succeeded: true })
            {
                return null;
            }

            if (authenticationResult.Ticket?.AuthenticationScheme
                != GuestAuthenticationScheme)
            {
                return null;
            }

            var principal = authenticationResult.Principal;

            if (principal?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            var guestIdClaims = principal
                .FindAll(ClaimTypes.NameIdentifier)
                .ToArray();

            if (guestIdClaims.Length != 1)
            {
                return null;
            }

            if (!Guid.TryParse(
                    guestIdClaims[0].Value,
                    out var guestId))
            {
                return null;
            }

            return guestId == Guid.Empty
                ? null
                : guestId;
        }
    }
}