using System.ComponentModel.DataAnnotations;

namespace VibyRoom.Api.Contracts.Music;

public sealed class TrackDetailsQuery
{
    [Required]
    [MaxLength(64)]
    public string? Provider { get; init; }

    [Required]
    [RegularExpression(
        "^[A-Za-z0-9]{1,128}$",
        ErrorMessage = "TrackId has an invalid format.")]
    public string? TrackId { get; init; }
}