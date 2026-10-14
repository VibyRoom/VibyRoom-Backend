using Microsoft.EntityFrameworkCore;
using VibyRoom.Application.Abstractions.Repositories;
using VibyRoom.Domain.Core.Entities.Rooms;

namespace VibyRoom.Infrastructure.Persistence.Repositories;

public sealed class RoomRepository : IRoomRepository
{
    private readonly VibyRoomDbContext _dbContext;

    public RoomRepository(VibyRoomDbContext dbContext)
    {
        _dbContext = dbContext
                     ?? throw new ArgumentNullException(nameof(dbContext));
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
}