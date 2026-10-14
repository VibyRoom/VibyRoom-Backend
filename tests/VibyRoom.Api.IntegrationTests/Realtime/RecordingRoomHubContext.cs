using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using VibyRoom.Api.Realtime;
using VibyRoom.Api.Realtime.Contracts;

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

    public Action? AfterAdd { get; set; }

    public IReadOnlyList<RoomPresenceSnapshot> PresenceFor(
        string connectionId)
    {
        return _presence.TryGetValue(
            connectionId, out var events)
            ? events.ToArray()
            : [];
    }

    public IReadOnlyList<RoomSubscriptionRevoked> RevocationsFor(
        string connectionId)
    {
        return _revocations.TryGetValue(
            connectionId, out var events)
            ? events.ToArray()
            : [];
    }

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
                    "Simulated delivery failure.");
            }

            foreach (var connectionId in _connectionIds)
            {
                _owner._presence
                    .GetOrAdd(
                        connectionId,
                        _ => new ConcurrentQueue<
                            RoomPresenceSnapshot>())
                    .Enqueue(snapshot);
            }

            return Task.CompletedTask;
        }

        public Task RoomSubscriptionRevoked(
            RoomSubscriptionRevoked payload)
        {
            foreach (var connectionId in _connectionIds)
            {
                _owner._revocations
                    .GetOrAdd(
                        connectionId,
                        _ => new ConcurrentQueue<
                            RoomSubscriptionRevoked>())
                    .Enqueue(payload);
            }

            return Task.CompletedTask;
        }
    }
}