namespace VibyRoom.Api.Contracts;

public sealed record AntiforgeryTokenResponse(
    string RequestToken,
    string HeaderName);