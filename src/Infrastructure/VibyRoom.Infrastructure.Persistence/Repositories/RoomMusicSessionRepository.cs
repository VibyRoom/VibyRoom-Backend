using Microsoft.EntityFrameworkCore;
using VibyRoom.Application.Abstractions.Repositories;
using VibyRoom.Domain.Music.Queue;

namespace VibyRoom.Infrastructure.Persistence.Repositories;

public sealed class RoomMusicSessionRepository
    : IRoomMusicSessionRepository
{
    private readonly VibyRoomDbContext _dbContext;

    public RoomMusicSessionRepository(
        VibyRoomDbContext dbContext)
    {
        _dbContext = dbContext
                     ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public Task<RoomMusicSession?> GetByRoomIdAsync(
        Guid roomId,
        CancellationToken cancellationToken = default)
    {
        if (roomId == Guid.Empty)
        {
            throw new ArgumentException(
                "Room ID cannot be empty.",
                nameof(roomId));
        }

        return _dbContext.Set<RoomMusicSession>()
            .AsTracking()
            .Include(session => session.PendingItems
                .OrderBy(item => item.Position))
            .SingleOrDefaultAsync(
                session => session.RoomId == roomId,
                cancellationToken);
    }

    public async Task AddAsync(
        RoomMusicSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        await _dbContext.Set<RoomMusicSession>().AddAsync(
            session,
            cancellationToken);
    }

    public async Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        await _dbContext.SaveChangesAsync(
            cancellationToken);
    }
}