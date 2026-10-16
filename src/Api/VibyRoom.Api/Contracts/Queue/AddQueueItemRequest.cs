using VibyRoom.Api.Contracts.Validation;

namespace VibyRoom.Api.Contracts.Queue;

public sealed class AddQueueItemRequest
{
    [TrimmedRequiredString(32)]
    public string? Provider { get; init; }

    [TrimmedRequiredString(256)]
    public string? TrackId { get; init; }
}