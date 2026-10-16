using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using VibyRoom.Api.Realtime;
using VibyRoom.Api.Realtime.Contracts;
using VibyRoom.Application.Contracts.Playback;

namespace VibyRoom.Api.IntegrationTests.Realtime;

public sealed class RecordingRoomHubContext
    : IHubContext<RoomHub, IRoomHubClient>
{
    private readonly object _sync = new();

    private readonly Dictionary<string, HashSet<string>> _groups =
        new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<
        string, ConcurrentQueue<RoomPresenceSnapshot>>
        _presence = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<
        string, ConcurrentQueue<RoomSubscriptionRevoked>>
        _revocations = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<
        string, ConcurrentQueue<RoomMemberJoined>>
        _memberJoined = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<
        string, ConcurrentQueue<RoomMemberLeft>>
        _memberLeft = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<
        string, ConcurrentQueue<RoomMemberRemoved>>
        _memberRemoved = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<
        string, ConcurrentQueue<RoomOwnerChanged>>
        _ownerChanged = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<
        string, ConcurrentQueue<RoomClosed>>
        _roomClosed = new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<
        string, ConcurrentQueue<QueueChanged>>
        _queueChanged = new(StringComparer.Ordinal);
    
    private readonly ConcurrentDictionary<
            string, ConcurrentQueue<RoomMusicStateDetails>>
        _musicStateChanged = new(StringComparer.Ordinal);

    public RecordingRoomHubContext()
    {
        Clients = new RecordingClients(this);
        Groups = new RecordingGroupManager(this);
    }

    public IHubClients<IRoomHubClient> Clients { get; }

    public IGroupManager Groups { get; }

    public bool FailAdd { get; set; }

    public bool FailRemove { get; set; }

    public bool FailPresenceDelivery { get; set; }

    public bool FailRoomEventDelivery { get; set; }

    public Action? AfterAdd { get; set; }

    public IReadOnlyList<RoomPresenceSnapshot> PresenceFor(
        string connectionId) =>
        GetEvents(_presence, connectionId);

    public IReadOnlyList<RoomSubscriptionRevoked> RevocationsFor(
        string connectionId) =>
        GetEvents(_revocations, connectionId);

    public IReadOnlyList<RoomMemberJoined> MemberJoinedFor(
        string connectionId) =>
        GetEvents(_memberJoined, connectionId);

    public IReadOnlyList<RoomMemberLeft> MemberLeftFor(
        string connectionId) =>
        GetEvents(_memberLeft, connectionId);

    public IReadOnlyList<RoomMemberRemoved> MemberRemovedFor(
        string connectionId) =>
        GetEvents(_memberRemoved, connectionId);

    public IReadOnlyList<RoomOwnerChanged> OwnerChangedFor(
        string connectionId) =>
        GetEvents(_ownerChanged, connectionId);

    public IReadOnlyList<RoomClosed> RoomClosedFor(
        string connectionId) =>
        GetEvents(_roomClosed, connectionId);

    public IReadOnlyList<QueueChanged> QueueChangedFor(
        string connectionId) =>
        GetEvents(_queueChanged, connectionId);
    
    public IReadOnlyList<RoomMusicStateDetails> MusicStateChangedFor(
        string connectionId) =>
        GetEvents(_musicStateChanged, connectionId);

    public IReadOnlyList<string> ConnectionsInGroup(
        string groupName)
    {
        lock (_sync)
        {
            return _groups.TryGetValue(groupName, out var ids)
                ? ids.Order(StringComparer.Ordinal).ToArray()
                : [];
        }
    }

    private static IReadOnlyList<T> GetEvents<T>(
        ConcurrentDictionary<string, ConcurrentQueue<T>> storage,
        string connectionId)
    {
        return storage.TryGetValue(connectionId, out var events)
            ? events.ToArray()
            : [];
    }

    private static void RecordEvent<T>(
        ConcurrentDictionary<string, ConcurrentQueue<T>> storage,
        IReadOnlyList<string> connectionIds,
        T payload)
    {
        foreach (var connectionId in connectionIds)
        {
            storage
                .GetOrAdd(
                    connectionId,
                    _ => new ConcurrentQueue<T>())
                .Enqueue(payload);
        }
    }

    private sealed class RecordingGroupManager : IGroupManager
    {
        private readonly RecordingRoomHubContext _owner;

        public RecordingGroupManager(
            RecordingRoomHubContext owner)
        {
            _owner = owner;
        }

        public Task AddToGroupAsync(
            string connectionId,
            string groupName,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_owner.FailAdd)
            {
                throw new InvalidOperationException(
                    "Simulated group addition failure.");
            }

            lock (_owner._sync)
            {
                if (!_owner._groups.TryGetValue(
                    groupName, out var ids))
                {
                    ids = new HashSet<string>(
                        StringComparer.Ordinal);

                    _owner._groups.Add(groupName, ids);
                }

                ids.Add(connectionId);
            }

            _owner.AfterAdd?.Invoke();

            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(
            string connectionId,
            string groupName,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_owner.FailRemove)
            {
                throw new InvalidOperationException(
                    "Simulated group removal failure.");
            }

            lock (_owner._sync)
            {
                if (_owner._groups.TryGetValue(
                    groupName, out var ids))
                {
                    ids.Remove(connectionId);

                    if (ids.Count == 0)
                    {
                        _owner._groups.Remove(groupName);
                    }
                }
            }

            return Task.CompletedTask;
        }
    }

    private sealed class RecordingClients
        : IHubClients<IRoomHubClient>
    {
        private readonly RecordingRoomHubContext _owner;

        public RecordingClients(
            RecordingRoomHubContext owner)
        {
            _owner = owner;
        }

        public IRoomHubClient All =>
            throw new NotSupportedException();

        public IRoomHubClient AllExcept(
            IReadOnlyList<string> excludedConnectionIds) =>
            throw new NotSupportedException();

        public IRoomHubClient Client(string connectionId) =>
            new RecordingClient(_owner, [connectionId]);

        public IRoomHubClient Clients(
            IReadOnlyList<string> connectionIds) =>
            new RecordingClient(_owner, connectionIds.ToArray());

        public IRoomHubClient Group(string groupName) =>
            throw new NotSupportedException();

        public IRoomHubClient GroupExcept(
            string groupName,
            IReadOnlyList<string> excludedConnectionIds) =>
            throw new NotSupportedException();

        public IRoomHubClient Groups(
            IReadOnlyList<string> groupNames) =>
            throw new NotSupportedException();

        public IRoomHubClient User(string userId) =>
            throw new NotSupportedException();

        public IRoomHubClient Users(
            IReadOnlyList<string> userIds) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingClient : IRoomHubClient
    {
        private readonly RecordingRoomHubContext _owner;
        private readonly IReadOnlyList<string> _connectionIds;

        public RecordingClient(
            RecordingRoomHubContext owner,
            IReadOnlyList<string> connectionIds)
        {
            _owner = owner;
            _connectionIds = connectionIds;
        }

        public Task PresenceChanged(
            RoomPresenceSnapshot snapshot)
        {
            if (_owner.FailPresenceDelivery)
            {
                throw new InvalidOperationException(
                    "Simulated presence delivery failure.");
            }

            RecordEvent(
                _owner._presence,
                _connectionIds,
                snapshot);

            return Task.CompletedTask;
        }

        public Task RoomSubscriptionRevoked(
            RoomSubscriptionRevoked payload)
        {
            RecordEvent(
                _owner._revocations,
                _connectionIds,
                payload);

            return Task.CompletedTask;
        }

        public Task RoomMemberJoined(
            RoomMemberJoined payload)
        {
            ThrowIfRoomEventDeliveryFails();

            RecordEvent(
                _owner._memberJoined,
                _connectionIds,
                payload);

            return Task.CompletedTask;
        }

        public Task RoomMemberLeft(
            RoomMemberLeft payload)
        {
            ThrowIfRoomEventDeliveryFails();

            RecordEvent(
                _owner._memberLeft,
                _connectionIds,
                payload);

            return Task.CompletedTask;
        }

        public Task RoomMemberRemoved(
            RoomMemberRemoved payload)
        {
            ThrowIfRoomEventDeliveryFails();

            RecordEvent(
                _owner._memberRemoved,
                _connectionIds,
                payload);

            return Task.CompletedTask;
        }

        public Task RoomOwnerChanged(
            RoomOwnerChanged payload)
        {
            ThrowIfRoomEventDeliveryFails();

            RecordEvent(
                _owner._ownerChanged,
                _connectionIds,
                payload);

            return Task.CompletedTask;
        }

        public Task RoomClosed(
            RoomClosed payload)
        {
            ThrowIfRoomEventDeliveryFails();

            RecordEvent(
                _owner._roomClosed,
                _connectionIds,
                payload);

            return Task.CompletedTask;
        }

        public Task QueueChanged(
            QueueChanged payload)
        {
            ThrowIfRoomEventDeliveryFails();

            RecordEvent(
                _owner._queueChanged,
                _connectionIds,
                payload);

            return Task.CompletedTask;
        }
        
        public Task MusicStateChanged(
            RoomMusicStateDetails payload)
        {
            ArgumentNullException.ThrowIfNull(payload);

            ThrowIfRoomEventDeliveryFails();

            RecordEvent(
                _owner._musicStateChanged,
                _connectionIds,
                payload);

            return Task.CompletedTask;
        }

        private void ThrowIfRoomEventDeliveryFails()
        {
            if (_owner.FailRoomEventDelivery)
            {
                throw new InvalidOperationException(
                    "Simulated room event delivery failure.");
            }
        }
    }
}