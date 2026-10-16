using System.ComponentModel.DataAnnotations;

namespace VibyRoom.Api.Contracts.Queue;

public sealed class MoveQueueItemRequest
{
    [Required]
    [Range(0, int.MaxValue)]
    public int? Position { get; init; }
}