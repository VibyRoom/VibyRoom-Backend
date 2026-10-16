using System.ComponentModel.DataAnnotations;

namespace VibyRoom.Api.Contracts.Music;

public sealed class TrackSearchQuery
{
    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string? Query { get; init; }

    [Range(1, 50)]
    public int Limit { get; init; } = 20;
}