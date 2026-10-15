using System.Text.Json.Serialization;

namespace VibyRoom.Infrastructure.Music.Dto;

internal sealed class AudiusArtworkDto
{
    [JsonPropertyName("150x150")]
    public string? SmallUrl { get; init; }

    [JsonPropertyName("480x480")]
    public string? MediumUrl { get; init; }

    [JsonPropertyName("1000x1000")]
    public string? LargeUrl { get; init; }
}