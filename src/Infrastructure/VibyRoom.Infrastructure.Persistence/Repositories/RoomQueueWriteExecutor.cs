using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using VibyRoom.Application.Abstractions.Exceptions;
using VibyRoom.Application.Abstractions.Repositories;
using VibyRoom.Domain.Core.Entities.Rooms;

namespace VibyRoom.Infrastructure.Persistence.Repositories;

public sealed class RoomQueueWriteExecutor
    : IRoomQueueWriteExecutor
{
    private const string QueuePositionConstraint =
        "uq_room_music_queue_items_room_position";

    private readonly VibyRoomDbContext _dbContext;

    public RoomQueueWriteExecutor(
        VibyRoomDbContext dbContext)
    {
        _dbContext = dbContext
            ?? throw new ArgumentNullException(
                nameof(dbContext));
    }

    public async Task<TResult> ExecuteAsync<TResult>(
        Guid roomId,
        Func<Room?, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        if (roomId == Guid.Empty)
        {
            throw new ArgumentException(
                "Room ID cannot be empty.",
                nameof(roomId));
        }

        ArgumentNullException.ThrowIfNull(operation);

        cancellationToken.ThrowIfCancellationRequested();

        if (_dbContext.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "A room queue write transaction is already active.");
        }

        _dbContext.ChangeTracker.DetectChanges();

        if (_dbContext.ChangeTracker.HasChanges())
        {
            throw new InvalidOperationException(
                "A room queue transaction requires a clean " +
                "DbContext without pending modifications.");
        }

        try
        {
            await using var transaction =
                await _dbContext.Database.BeginTransactionAsync(
                    IsolationLevel.ReadCommitted,
                    cancellationToken);

            var roomExists = await LockRoomAsync(
                roomId,
                cancellationToken);

            // Discard any stale tracked Room that might have
            // been loaded before the PostgreSQL row lock.
            _dbContext.ChangeTracker.Clear();

            Room? room = null;

            if (roomExists)
            {
                room = await _dbContext.Rooms
                    .AsTracking()
                    .Include(current => current.Members)
                    .SingleOrDefaultAsync(
                        current => current.Id == roomId,
                        cancellationToken);
            }

            // Application must recheck Room status,
            // membership and role inside this callback.
            var result = await operation(
                room,
                cancellationToken);

            await _dbContext.SaveChangesAsync(
                cancellationToken);

            await transaction.CommitAsync(
                cancellationToken);

            return result;
        }
        catch (PostgresException exception)
            when (IsQueuePositionConflict(exception))
        {
            throw new RoomQueueConcurrencyException(
                "The music queue order conflicted with " +
                "another database operation.",
                exception);
        }
    }

    private async Task<bool> LockRoomAsync(
        Guid roomId,
        CancellationToken cancellationToken)
    {
        var connection =
            _dbContext.Database.GetDbConnection();

        var transaction =
            _dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "An active database transaction is required.");

        await using var command =
            connection.CreateCommand();

        command.Transaction =
            transaction.GetDbTransaction();

        command.CommandText =
            "SELECT id FROM rooms " +
            "WHERE id = @roomId FOR UPDATE";

        var parameter = command.CreateParameter();

        parameter.ParameterName = "roomId";
        parameter.Value = roomId;

        command.Parameters.Add(parameter);

        var result = await command.ExecuteScalarAsync(
            cancellationToken);

        return result is not null and not DBNull;
    }

    private static bool IsQueuePositionConflict(
        PostgresException exception)
    {
        return exception.SqlState ==
                   PostgresErrorCodes.UniqueViolation
               && exception.ConstraintName ==
                   QueuePositionConstraint;
    }
}