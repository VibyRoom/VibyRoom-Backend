namespace VibyRoom.Api.Authentication;

internal static class GuestAntiforgeryExtensions
{
    public const string HeaderName = "X-CSRF-TOKEN";

    private const string CookieName = "VibyRoom.Antiforgery";

    public static IServiceCollection AddGuestAntiforgery(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddAntiforgery(options =>
        {
            options.HeaderName = HeaderName;

            options.Cookie.Name = CookieName;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.Path = "/";
            options.Cookie.IsEssential = true;
        });

        return services;
    }
}