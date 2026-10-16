using VibyRoom.Domain.Music.Queue;

namespace VibyRoom.Application.Abstractions.Repositories;

public interface IRoomMusicSessionRepository
{
    Task<RoomMusicSession?> GetByRoomIdAsync(
        Guid roomId,
        CancellationToken cancellationToken = default);

    Task<List<Guid>> GetDueRoomIdsAsync(
        DateTimeOffset nowUtc,
        int batchSize,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        RoomMusicSession session,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);
}