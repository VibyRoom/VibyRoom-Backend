using System.Text.Json;
using VibyRoom.Domain.Music.Playback;

namespace VibyRoom.Infrastructure.Persistence.Configurations;

internal static class CurrentPlaybackItemJsonSerializer
{
    public static string? Serialize(CurrentPlaybackItem? item)
    {
        return item is null ? null : JsonSerializer.Serialize(item);
    }

    public static CurrentPlaybackItem? Deserialize(string? json)
    {
        return json is null
            ? null
            : JsonSerializer.Deserialize<CurrentPlaybackItem>(json)
              ?? throw new JsonException("Current playback item is null.");
    }
}