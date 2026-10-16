using System.Text.Json.Serialization;

namespace VibyRoom.Infrastructure.Music.Dto;

internal sealed class AudiusTrackDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("duration")]
    public long? DurationSeconds { get; init; }

    [JsonPropertyName("user")]
    public AudiusUserDto? User { get; init; }

    [JsonPropertyName("artwork")]
    public AudiusArtworkDto? Artwork { get; init; }

    [JsonPropertyName("permalink")]
    public string? Permalink { get; init; }

    [JsonPropertyName("license")]
    public string? License { get; init; }

    [JsonPropertyName("is_streamable")]
    public bool? IsStreamable { get; init; }

    [JsonPropertyName("is_stream_gated")]
    public bool? IsStreamGated { get; init; }
}