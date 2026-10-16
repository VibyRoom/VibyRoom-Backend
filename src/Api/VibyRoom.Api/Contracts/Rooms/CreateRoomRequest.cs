using VibyRoom.Api.Contracts.Validation;

namespace VibyRoom.Api.Contracts.Rooms;

public sealed class CreateRoomRequest
{
    [TrimmedRequiredString(100)]
    public string? Name { get; init; }

    [TrimmedRequiredString(40)]
    public string? DisplayName { get; init; }
}