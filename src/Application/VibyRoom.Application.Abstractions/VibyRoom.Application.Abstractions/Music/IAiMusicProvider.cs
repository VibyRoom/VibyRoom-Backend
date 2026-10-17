namespace VibyRoom.Application.Abstractions.Music;

public interface IAiMusicProvider
{
    Task<string> GenerateAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);
}