using System.Text.Json.Serialization;

namespace VibyRoom.Infrastructure.Music.Dto;

internal sealed class AudiusUserDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("handle")]
    public string? Handle { get; init; }
}