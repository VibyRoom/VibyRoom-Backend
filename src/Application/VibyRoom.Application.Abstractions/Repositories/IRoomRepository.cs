using VibyRoom.Domain.Core.Entities.Rooms;

namespace VibyRoom.Application.Abstractions.Repositories;

public interface IRoomRepository
{
    Task<Room?> GetByIdAsync(
        Guid roomId,
        CancellationToken cancellationToken = default);

    Task<Room?> GetByInviteCodeAsync(
        string inviteCode,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        Room room,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(
        CancellationToken cancellationToken = default);
}