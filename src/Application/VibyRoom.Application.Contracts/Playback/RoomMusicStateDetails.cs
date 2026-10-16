using VibyRoom.Application.Contracts.Queue;

namespace VibyRoom.Application.Contracts.Playback;

public sealed record RoomMusicStateDetails(
    Guid RoomId,
    long Version,
    IReadOnlyList<QueueItemDetails> Queue,
    PlaybackDetails Playback,
    DateTimeOffset ServerTimeUtc);