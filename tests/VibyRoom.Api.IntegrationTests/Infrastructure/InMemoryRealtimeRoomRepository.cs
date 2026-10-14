using System.Collections.Concurrent;
using VibyRoom.Application.Abstractions.Repositories;
using VibyRoom.Domain.Core.Entities.Rooms;

namespace VibyRoom.Api.IntegrationTests.Realtime.Infrastructure;

public sealed class InMemoryRealtimeRoomRepository : IRoomRepository
{
    private readonly ConcurrentDictionary<Guid, Room> _rooms = new();

    public Room SeedRoom(
        Guid ownerGuestId,
        params Guid[] memberGuestIds)
    {
        var inviteCode = Guid.NewGuid()
            .ToString("N")[..22];

        var room = Room.Create(
            "Realtime test room",
            inviteCode,
            ownerGuestId,
            "Owner",
            20,
            DateTimeOffset.UtcNow);

        foreach (var memberGuestId in memberGuestIds)
        {
            room.Join(
                memberGuestId,
                "Member",
                inviteCode,
                DateTimeOffset.UtcNow);
        }

        if (!_rooms.TryAdd(room.Id, room))
        {
            throw new InvalidOperationException(
                "Failed to seed test room.");
        }

        return room;
    }

    public Task<Room?> GetByIdAsync(
        Guid roomId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _rooms.TryGetValue(roomId, out var room);

        return Task.FromResult(room);
    }

    public Task<Room?> GetByInviteCodeAsync(
        string inviteCode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var room = _rooms.Values.FirstOrDefault(
            candidate => candidate.InviteCode == inviteCode);

        return Task.FromResult(room);
    }

    public Task AddAsync(
        Room room,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(room);

        if (!_rooms.TryAdd(room.Id, room))
        {
            throw new InvalidOperationException(
                "Room already exists.");
        }

        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.CompletedTask;
    }
}