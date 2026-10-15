namespace VibyRoom.Infrastructure.Music.Configuration;

public sealed class AudiusProviderOptions
{
    public const string SectionName = "MusicProvider";
    public const string ProviderId = "audius";

    public string Provider { get; set; } = ProviderId;

    public string BaseUrl { get; set; } =
        "https://api.audius.co/v1/";

    public string? BearerToken { get; set; }

    public int TimeoutSeconds { get; set; } = 15;
}