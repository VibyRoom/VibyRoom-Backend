using System.ComponentModel.DataAnnotations;

namespace VibyRoom.Api.Contracts.Ai;

public sealed class GenerateAiPlaylistRequest
{
    [Required]
    [MinLength(3)]
    [MaxLength(500)]
    public string? Prompt { get; init; }
}