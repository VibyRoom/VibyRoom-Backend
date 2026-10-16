using VibyRoom.Api.Contracts.Queue;

namespace VibyRoom.Api.Contracts.Playback;

public sealed record RoomMusicStateResponse(
    Guid RoomId,
    long Version,
    QueueItemResponse[] Queue,
    PlaybackResponse Playback,
    DateTimeOffset ServerTimeUtc);