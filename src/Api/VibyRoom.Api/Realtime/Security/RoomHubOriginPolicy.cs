namespace VibyRoom.Api.Realtime.Security;

public sealed class RoomHubOriginPolicy
{
    public const string CorsPolicyName = "RoomHubCors";

    private readonly HashSet<string> _allowedOrigins;

    public IReadOnlyList<string> AllowedOrigins { get; }

    public RoomHubOriginPolicy(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var configuredOrigins = configuration
            .GetSection("Realtime:AllowedOrigins")
            .Get<string[]>() ?? [];

        if (configuredOrigins.Length == 0)
        {
            throw new InvalidOperationException(
                "Realtime:AllowedOrigins must contain " +
                "at least one explicitly allowed origin.");
        }

        _allowedOrigins = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var origin in configuredOrigins)
        {
            if (!IsValidOrigin(origin))
            {
                throw new InvalidOperationException(
                    "Realtime:AllowedOrigins contains " +
                    "an invalid origin. Expected an HTTP(S) " +
                    "origin without path, query or fragment.");
            }

            _allowedOrigins.Add(origin);
        }

        AllowedOrigins = Array.AsReadOnly(
            _allowedOrigins
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    public bool IsAllowed(string? origin)
    {
        return !string.IsNullOrWhiteSpace(origin)
               && _allowedOrigins.Contains(origin);
    }

    private static bool IsValidOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin) ||
            origin != origin.Trim() ||
            !Uri.TryCreate(
                origin,
                UriKind.Absolute,
                out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp &&
            uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        return string.Equals(
            origin,
            uri.GetLeftPart(UriPartial.Authority),
            StringComparison.OrdinalIgnoreCase);
    }
}