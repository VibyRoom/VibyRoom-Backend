using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace VibyRoom.Api.Authentication;

public static class GuestAuthenticationExtensions
{
    public static IServiceCollection AddGuestAuthentication(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme =
                    GuestAuthenticationDefaults.AuthenticationScheme;

                options.DefaultChallengeScheme =
                    GuestAuthenticationDefaults.AuthenticationScheme;

                options.DefaultSignInScheme =
                    GuestAuthenticationDefaults.AuthenticationScheme;
            })
            .AddCookie(
                GuestAuthenticationDefaults.AuthenticationScheme,
                options =>
                {
                    options.Cookie.Name =
                        GuestAuthenticationDefaults.CookieName;

                    options.Cookie.HttpOnly = true;
                    options.Cookie.SecurePolicy =
                        CookieSecurePolicy.Always;

                    options.Cookie.SameSite = SameSiteMode.Lax;
                    options.Cookie.Path = "/";

                    options.ExpireTimeSpan =
                        GuestAuthenticationDefaults.SessionLifetime;

                    options.SlidingExpiration = true;

                    options.Events = new CookieAuthenticationEvents
                    {
                        OnValidatePrincipal = ValidatePrincipalAsync,

                        OnRedirectToLogin = context =>
                        {
                            context.Response.StatusCode =
                                StatusCodes.Status401Unauthorized;

                            return Task.CompletedTask;
                        },

                        OnRedirectToAccessDenied = context =>
                        {
                            context.Response.StatusCode =
                                StatusCodes.Status403Forbidden;

                            return Task.CompletedTask;
                        }
                    };
                });

        services.AddAuthorization();

        return services;
    }

    private static Task ValidatePrincipalAsync(
        CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;

        if (principal?.Identity is not ClaimsIdentity identity ||
            !identity.IsAuthenticated ||
            identity.AuthenticationType !=
                GuestAuthenticationDefaults.AuthenticationScheme)
        {
            context.RejectPrincipal();
            return Task.CompletedTask;
        }

        var guestIdClaims = principal
            .FindAll(GuestAuthenticationDefaults.GuestIdClaimType)
            .ToArray();

        if (guestIdClaims.Length != 1 ||
            !Guid.TryParse(guestIdClaims[0].Value, out var guestId) ||
            guestId == Guid.Empty)
        {
            context.RejectPrincipal();
        }

        return Task.CompletedTask;
    }
}