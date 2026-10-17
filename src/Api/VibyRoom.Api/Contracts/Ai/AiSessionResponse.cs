namespace VibyRoom.Api.Contracts.Ai;

public sealed record AiSessionResponse(
    string? Prompt,
    bool CanGenerateMore);