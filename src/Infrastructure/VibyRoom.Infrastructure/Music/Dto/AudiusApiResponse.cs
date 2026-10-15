using System.Text.Json.Serialization;

namespace VibyRoom.Infrastructure.Music.Dto;

internal sealed class AudiusApiResponse<T>
{
    [JsonPropertyName("data")]
    public T? Data { get; init; }
}