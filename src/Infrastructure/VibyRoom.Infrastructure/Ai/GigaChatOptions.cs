namespace VibyRoom.Infrastructure.Ai;

public sealed class GigaChatOptions
{
    public const string SectionName = "GigaChat";

    public const string OAuthClientName = "GigaChatOAuth";

    public const string ChatClientName = "GigaChatChat";

    public string? AuthorizationKey { get; set; }

    public string? TrustedRootCertificatePath { get; set; }

    public int TimeoutSeconds { get; set; } = 60;
}