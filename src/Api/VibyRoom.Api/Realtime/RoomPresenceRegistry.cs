using VibyRoom.Api.Realtime.Contracts;

namespace VibyRoom.Api.Realtime;

public sealed class RoomPresenceRegistry
{
    private readonly object _sync = new();

    private readonly Dictionary<Guid, Dictionary<Guid, HashSet<string>>>
        _connectionsByRoom = new();

    private readonly Dictionary<string, (Guid GuestId, HashSet<Guid> RoomIds)>
        _roomsByConnection = new(StringComparer.Ordinal);

    public RoomPresenceMutation Register(
        Guid roomId,
        Guid guestId,
        string connectionId)
    {
        ValidateRoomId(roomId);
        ValidateGuestId(guestId);
        ValidateConnectionId(connectionId);

        lock (_sync)
        {
            if (_roomsByConnection.TryGetValue(
                    connectionId,
                    out var existing))
            {
                if (existing.GuestId != guestId)
                {
                    throw new InvalidOperationException(
                        "Connection belongs to another guest.");
                }

                if (existing.RoomIds.Contains(roomId))
                {
                    return new RoomPresenceMutation(
                        false,
                        false,
                        CreateSnapshot(roomId));
                }
            }

            if (!_connectionsByRoom.TryGetValue(
                    roomId,
                    out var guests))
            {
                guests = new Dictionary<Guid, HashSet<string>>();
                _connectionsByRoom.Add(roomId, guests);
            }

            var wasOnline = guests.TryGetValue(
                guestId,
                out var connectionIds);

            if (!wasOnline)
            {
                connectionIds = new HashSet<string>(
                    StringComparer.Ordinal);

                guests.Add(guestId, connectionIds);
            }

            connectionIds!.Add(connectionId);

            if (_roomsByConnection.TryGetValue(
                    connectionId,
                    out var connection))
            {
                connection.RoomIds.Add(roomId);
            }
            else
            {
                _roomsByConnection.Add(
                    connectionId,
                    (guestId, new HashSet<Guid> { roomId }));
            }

            return new RoomPresenceMutation(
                true,
                !wasOnline,
                CreateSnapshot(roomId));
        }
    }

    public RoomPresenceMutation Unregister(
        Guid roomId,
        string connectionId)
    {
        ValidateRoomId(roomId);
        ValidateConnectionId(connectionId);

        lock (_sync)
        {
            if (!_roomsByConnection.TryGetValue(
                    connectionId,
                    out var connection) ||
                !connection.RoomIds.Contains(roomId))
            {
                return new RoomPresenceMutation(
                    false,
                    false,
                    CreateSnapshot(roomId));
            }

            return RemoveConnectionFromRoom(
                roomId,
                connectionId,
                connection.GuestId,
                connection.RoomIds);
        }
    }

    public IReadOnlyList<RoomPresenceMutation> UnregisterConnection(
        string connectionId)
    {
        ValidateConnectionId(connectionId);

        lock (_sync)
        {
            if (!_roomsByConnection.TryGetValue(
                    connectionId,
                    out var connection))
            {
                return Array.Empty<RoomPresenceMutation>();
            }

            var roomIds = connection.RoomIds
                .Order()
                .ToArray();

            var results = new List<RoomPresenceMutation>(
                roomIds.Length);

            foreach (var roomId in roomIds)
            {
                results.Add(
                    RemoveConnectionFromRoom(
                        roomId,
                        connectionId,
                        connection.GuestId,
                        connection.RoomIds));
            }

            return results.AsReadOnly();
        }
    }

    public IReadOnlyList<Guid> GetRoomsForConnection(
        string connectionId)
    {
        ValidateConnectionId(connectionId);

        lock (_sync)
        {
            if (!_roomsByConnection.TryGetValue(
                    connectionId,
                    out var connection))
            {
                return Array.Empty<Guid>();
            }

            return Array.AsReadOnly(
                connection.RoomIds
                    .Order()
                    .ToArray());
        }
    }

    public IReadOnlyList<Guid> GetOnlineGuests(Guid roomId)
    {
        ValidateRoomId(roomId);

        lock (_sync)
        {
            return CreateSnapshot(roomId).OnlineGuestIds;
        }
    }

    public IReadOnlyList<string> GetConnectionIds(Guid roomId)
    {
        ValidateRoomId(roomId);

        lock (_sync)
        {
            if (!_connectionsByRoom.TryGetValue(
                    roomId,
                    out var guests))
            {
                return Array.Empty<string>();
            }

            return Array.AsReadOnly(
                guests.Values
                    .SelectMany(connections => connections)
                    .Order(StringComparer.Ordinal)
                    .ToArray());
        }
    }

    public RoomPresenceRevocation RevokeGuest(
        Guid roomId,
        Guid guestId)
    {
        ValidateRoomId(roomId);
        ValidateGuestId(guestId);

        lock (_sync)
        {
            if (!_connectionsByRoom.TryGetValue(
                    roomId,
                    out var guests) ||
                !guests.TryGetValue(
                    guestId,
                    out var connectionIds))
            {
                return new RoomPresenceRevocation(
                    Array.Empty<string>(),
                    false,
                    CreateSnapshot(roomId));
            }

            var revokedConnections = connectionIds
                .Order(StringComparer.Ordinal)
                .ToArray();

            foreach (var connectionId in revokedConnections)
            {
                var connection = _roomsByConnection[connectionId];

                connection.RoomIds.Remove(roomId);

                if (connection.RoomIds.Count == 0)
                {
                    _roomsByConnection.Remove(connectionId);
                }
            }

            guests.Remove(guestId);

            if (guests.Count == 0)
            {
                _connectionsByRoom.Remove(roomId);
            }

            return new RoomPresenceRevocation(
                Array.AsReadOnly(revokedConnections),
                true,
                CreateSnapshot(roomId));
        }
    }

    public IReadOnlyList<string> ClearRoom(Guid roomId)
    {
        ValidateRoomId(roomId);

        lock (_sync)
        {
            if (!_connectionsByRoom.TryGetValue(
                    roomId,
                    out var guests))
            {
                return Array.Empty<string>();
            }

            var connectionIds = guests.Values
                .SelectMany(connections => connections)
                .Order(StringComparer.Ordinal)
                .ToArray();

            foreach (var connectionId in connectionIds)
            {
                var connection = _roomsByConnection[connectionId];

                connection.RoomIds.Remove(roomId);

                if (connection.RoomIds.Count == 0)
                {
                    _roomsByConnection.Remove(connectionId);
                }
            }

            _connectionsByRoom.Remove(roomId);

            return Array.AsReadOnly(connectionIds);
        }
    }

    private RoomPresenceMutation RemoveConnectionFromRoom(
        Guid roomId,
        string connectionId,
        Guid guestId,
        HashSet<Guid> subscribedRoomIds)
    {
        var guests = _connectionsByRoom[roomId];
        var connectionIds = guests[guestId];

        connectionIds.Remove(connectionId);
        subscribedRoomIds.Remove(roomId);

        if (subscribedRoomIds.Count == 0)
        {
            _roomsByConnection.Remove(connectionId);
        }

        var becameOffline = connectionIds.Count == 0;

        if (becameOffline)
        {
            guests.Remove(guestId);

            if (guests.Count == 0)
            {
                _connectionsByRoom.Remove(roomId);
            }
        }

        return new RoomPresenceMutation(
            true,
            becameOffline,
            CreateSnapshot(roomId));
    }

    private RoomPresenceSnapshot CreateSnapshot(Guid roomId)
    {
        if (!_connectionsByRoom.TryGetValue(
                roomId,
                out var guests))
        {
            return new RoomPresenceSnapshot(
                roomId,
                Array.Empty<Guid>());
        }

        return new RoomPresenceSnapshot(
            roomId,
            guests.Keys.ToArray());
    }

    private static void ValidateRoomId(Guid roomId)
    {
        if (roomId == Guid.Empty)
        {
            throw new ArgumentException(
                "Room ID cannot be empty.",
                nameof(roomId));
        }
    }

    private static void ValidateGuestId(Guid guestId)
    {
        if (guestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Guest ID cannot be empty.",
                nameof(guestId));
        }
    }

    private static void ValidateConnectionId(string connectionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
    }
}