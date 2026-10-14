using System.Security.Claims;

namespace VibyRoom.Api.Authentication;

internal static class GuestAuthenticationDefaults
{
    public const string AuthenticationScheme = "Cookie";

    public const string CookieName = "VibyRoom.Guest";

    public const string GuestIdClaimType = ClaimTypes.NameIdentifier;

    public static readonly TimeSpan SessionLifetime =
        TimeSpan.FromDays(30);
}