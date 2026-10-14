namespace VibyRoom.Api.Contracts;

public sealed record GuestSessionResponse(
    Guid GuestId,
    DateTimeOffset ExpiresAtUtc);