using VibyRoom.Api.Contracts.Validation;

namespace VibyRoom.Api.Contracts.Rooms;

public sealed class JoinRoomRequest
{
    [TrimmedRequiredString(40)]
    public string? DisplayName { get; init; }
}