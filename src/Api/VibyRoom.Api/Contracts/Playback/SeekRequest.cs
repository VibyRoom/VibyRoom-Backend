using System.ComponentModel.DataAnnotations;

namespace VibyRoom.Api.Contracts.Playback;

public sealed class SeekRequest
{
    [Required]
    [Range(typeof(long), "0", "9223372036854775807")]
    public long? PositionMs { get; init; }
}