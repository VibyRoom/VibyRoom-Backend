using Microsoft.EntityFrameworkCore;
using VibyRoom.Application.Abstractions.Repositories;
using VibyRoom.Domain.Core.Entities.Rooms;
using VibyRoom.Domain.Core.Entities.Rooms.Enums;

namespace VibyRoom.Infrastructure.Persistence.Repositories;

public sealed class RoomRepository : IRoomRepository
{
    private readonly VibyRoomDbContext _dbContext;
    private readonly IRoomQueueWriteExecutor _writer;
    private readonly IRoomMusicSessionRepository _musicSessions;

    public RoomRepository(
        VibyRoomDbContext dbContext,
        IRoomQueueWriteExecutor writer,
        IRoomMusicSessionRepository musicSessions)
    {
        _dbContext = dbContext
            ?? throw new ArgumentNullException(nameof(dbContext));

        _writer = writer
            ?? throw new ArgumentNullException(nameof(writer));

        _musicSessions = musicSessions
            ?? throw new ArgumentNullException(nameof(musicSessions));
    }

    public Task<Room?> GetByIdAsync(
        Guid roomId,
        CancellationToken cancellationToken = default)
    {
        return _dbContext.Rooms
            .AsTracking()
            .Include(room => room.Members)
            .SingleOrDefaultAsync(
                room => room.Id == roomId,
                cancellationToken);
    }

    public Task<Room?> GetByInviteCodeAsync(
        string inviteCode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inviteCode);

        return _dbContext.Rooms
            .AsTracking()
            .Include(room => room.Members)
            .SingleOrDefaultAsync(
                room => room.InviteCode == inviteCode,
                cancellationToken);
    }

    public async Task AddAsync(
        Room room,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(room);

        await _dbContext.Rooms.AddAsync(
            room,
            cancellationToken);
    }

    public async Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<Room> ExecuteLifecycleMutationAsync(
        Guid roomId,
        Func<Room?, Room> mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);

        return _writer.ExecuteAsync(
            roomId,
            async (room, ct) =>
            {
                // Room уже заблокирована через SELECT FOR UPDATE.
                var updated = mutation(room);

                if (room is null ||
                    !ReferenceEquals(updated, room) ||
                    updated.Id != roomId)
                {
                    throw new InvalidOperationException(
                        "Lifecycle mutation must modify the locked Room.");
                }

                if (updated.Status == RoomStatus.Closed)
                {
                    // Загружаем музыкальный агрегат после блокировки Room.
                    var session = await _musicSessions.GetByRoomIdAsync(
                        roomId,
                        ct);

                    session?.StopForRoomClosure();
                }

                // Executor сохранит Room и MusicSession
                // одним SaveChanges в одной транзакции.
                return updated;
            },
            cancellationToken);
    }
}