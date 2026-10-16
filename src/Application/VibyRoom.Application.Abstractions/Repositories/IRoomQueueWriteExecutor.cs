using VibyRoom.Domain.Core.Entities.Rooms;

namespace VibyRoom.Application.Abstractions.Repositories;

public interface IRoomQueueWriteExecutor
{
    Task<TResult> ExecuteAsync<TResult>(
        Guid roomId,
        Func<Room?, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default);
}