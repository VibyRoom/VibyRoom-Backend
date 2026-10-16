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

    async Task<Room> ExecuteLifecycleMutationAsync(
        Guid roomId,
        Func<Room?, Room> mutation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mutation);

        var room = await GetByIdAsync(roomId, cancellationToken);
        var result = mutation(room);

        await SaveChangesAsync(cancellationToken);

        return result;
    }
}