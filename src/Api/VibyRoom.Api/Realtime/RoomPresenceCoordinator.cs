using Microsoft.AspNetCore.SignalR;
using VibyRoom.Api.Realtime.Contracts;
using VibyRoom.Application.Abstractions.Realtime;
using VibyRoom.Application.Abstractions.Rooms;

namespace VibyRoom.Api.Realtime;

public sealed class RoomPresenceCoordinator :
    IRoomPresenceNotifier,
    IDisposable
{
    private const int LockStripeCount = 64;

    private readonly RoomPresenceRegistry _registry;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<RoomHub, IRoomHubClient> _hubContext;
    private readonly ILogger<RoomPresenceCoordinator> _logger;

    private readonly SemaphoreSlim[] _roomLocks;
    private readonly SemaphoreSlim[] _connectionLocks;

    public RoomPresenceCoordinator(
        RoomPresenceRegistry registry,
        IServiceScopeFactory scopeFactory,
        IHubContext<RoomHub, IRoomHubClient> hubContext,
        ILogger<RoomPresenceCoordinator> logger)
    {
        _registry = registry
            ?? throw new ArgumentNullException(nameof(registry));

        _scopeFactory = scopeFactory
            ?? throw new ArgumentNullException(nameof(scopeFactory));

        _hubContext = hubContext
            ?? throw new ArgumentNullException(nameof(hubContext));

        _logger = logger
            ?? throw new ArgumentNullException(nameof(logger));

        _roomLocks = CreateLocks();
        _connectionLocks = CreateLocks();
    }

    public async Task<RoomPresenceSnapshot> SubscribeToRoomAsync(
        Guid roomId,
        Guid guestId,
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        ValidateRoom(roomId);
        ValidateGuest(guestId);
        ValidateConnection(connectionId);

        var connectionLock = GetConnectionLock(connectionId);
        var roomLock = GetRoomLock(roomId);

        await connectionLock.WaitAsync(cancellationToken);

        try
        {
            await roomLock.WaitAsync(cancellationToken);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                await using var scope =
                    _scopeFactory.CreateAsyncScope();

                var membershipQuery = scope.ServiceProvider
                    .GetRequiredService<IRoomMembershipQuery>();

                var isMember = await membershipQuery
                    .IsActiveMemberAsync(
                        roomId,
                        guestId,
                        cancellationToken);

                if (!isMember)
                {
                    throw new HubException("roomAccessDenied");
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (_registry
                    .GetRoomsForConnection(connectionId)
                    .Contains(roomId))
                {
                    return CreateSnapshot(roomId);
                }

                var groupName = GetGroupName(roomId);
                var groupAdded = false;

                try
                {
                    await _hubContext.Groups.AddToGroupAsync(
                        connectionId,
                        groupName,
                        cancellationToken);

                    groupAdded = true;

                    cancellationToken.ThrowIfCancellationRequested();

                    var mutation = _registry.Register(
                        roomId,
                        guestId,
                        connectionId);

                    if (mutation.OnlineGuestsChanged)
                    {
                        await PublishPresenceChangedAsync(
                            mutation.Snapshot);
                    }

                    return mutation.Snapshot;
                }
                catch
                {
                    if (groupAdded)
                    {
                        await TryRemoveFromGroupAsync(
                            connectionId,
                            groupName);
                    }

                    throw;
                }
            }
            finally
            {
                roomLock.Release();
            }
        }
        catch (HubException)
        {
            throw;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to subscribe to room {RoomId}.",
                roomId);

            throw new HubException("roomSubscriptionFailed");
        }
        finally
        {
            connectionLock.Release();
        }
    }

    public async Task UnsubscribeFromRoomAsync(
        Guid roomId,
        string connectionId,
        CancellationToken cancellationToken = default)
    {
        ValidateRoom(roomId);
        ValidateConnection(connectionId);

        var connectionLock = GetConnectionLock(connectionId);
        var roomLock = GetRoomLock(roomId);

        await connectionLock.WaitAsync(cancellationToken);

        try
        {
            await roomLock.WaitAsync(cancellationToken);

            try
            {
                if (!_registry
                    .GetRoomsForConnection(connectionId)
                    .Contains(roomId))
                {
                    return;
                }

                await TryRemoveFromGroupAsync(
                    connectionId,
                    GetGroupName(roomId));

                var mutation = _registry.Unregister(
                    roomId,
                    connectionId);

                if (mutation.OnlineGuestsChanged)
                {
                    await PublishPresenceChangedAsync(
                        mutation.Snapshot);
                }
            }
            finally
            {
                roomLock.Release();
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to unsubscribe from room {RoomId}.",
                roomId);

            throw new HubException("roomUnsubscriptionFailed");
        }
        finally
        {
            connectionLock.Release();
        }
    }

    public async Task DisconnectAsync(string connectionId)
    {
        ValidateConnection(connectionId);

        var connectionLock = GetConnectionLock(connectionId);

        await connectionLock.WaitAsync(CancellationToken.None);

        try
        {
            var roomIds = _registry
                .GetRoomsForConnection(connectionId);

            foreach (var roomId in roomIds)
            {
                var roomLock = GetRoomLock(roomId);

                await roomLock.WaitAsync(CancellationToken.None);

                try
                {
                    var mutation = _registry.Unregister(
                        roomId,
                        connectionId);

                    if (mutation.OnlineGuestsChanged)
                    {
                        await PublishPresenceChangedAsync(
                            mutation.Snapshot);
                    }
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        exception,
                        "Failed to clear disconnected connection "
                        + "from room {RoomId}.",
                        roomId);
                }
                finally
                {
                    roomLock.Release();
                }
            }
        }
        finally
        {
            connectionLock.Release();
        }
    }

    public async Task RevokeMemberAsync(
        Guid roomId,
        Guid guestId,
        CancellationToken cancellationToken = default)
    {
        ValidateRoom(roomId);
        ValidateGuest(guestId);

        var roomLock = GetRoomLock(roomId);
        var acquired = false;

        try
        {
            // Membership changes have already been committed.
            // Cleanup must not depend on HTTP request cancellation.
            await roomLock.WaitAsync(CancellationToken.None);
            acquired = true;

            await RevokeMemberCoreAsync(roomId, guestId);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to revoke membership presence "
                + "for room {RoomId} and guest {GuestId}.",
                roomId,
                guestId);
        }
        finally
        {
            if (acquired)
            {
                roomLock.Release();
            }
        }
    }

    public async Task RevokeRoomAsync(
        Guid roomId,
        CancellationToken cancellationToken = default)
    {
        ValidateRoom(roomId);

        var roomLock = GetRoomLock(roomId);
        var acquired = false;

        try
        {
            await roomLock.WaitAsync(CancellationToken.None);
            acquired = true;

            await RevokeRoomCoreAsync(roomId);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to revoke room {RoomId} subscriptions.",
                roomId);
        }
        finally
        {
            if (acquired)
            {
                roomLock.Release();
            }
        }
    }

    public async Task PublishRoomEventAsync(
        Guid roomId,
        Func<IRoomHubClient, Task> publish,
        CancellationToken cancellationToken = default)
    {
        ValidateRoom(roomId);
        ArgumentNullException.ThrowIfNull(publish);

        var roomLock = GetRoomLock(roomId);

        await roomLock.WaitAsync(cancellationToken);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            await PublishRoomEventCoreAsync(
                roomId,
                publish);
        }
        finally
        {
            roomLock.Release();
        }
    }

    public async Task RevokeMemberAndPublishAsync(
        Guid roomId,
        Guid guestId,
        Func<IRoomHubClient, Task> publish,
        CancellationToken cancellationToken = default)
    {
        ValidateRoom(roomId);
        ValidateGuest(guestId);
        ArgumentNullException.ThrowIfNull(publish);

        var roomLock = GetRoomLock(roomId);
        var acquired = false;

        try
        {
            // Use the same room lock as SubscribeToRoomAsync.
            // Revocation and the following event publication
            // are coordinated under one lock.
            await roomLock.WaitAsync(CancellationToken.None);
            acquired = true;

            await RevokeMemberCoreAsync(
                roomId,
                guestId);

            // The revoked guest is already excluded from
            // the Registry-based recipient allowlist.
            await PublishRoomEventCoreAsync(
                roomId,
                publish);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to revoke member and publish room event "
                + "for room {RoomId} and guest {GuestId}.",
                roomId,
                guestId);
        }
        finally
        {
            if (acquired)
            {
                roomLock.Release();
            }
        }
    }

    public async Task PublishRoomClosedAndRevokeAsync(
        Guid roomId,
        Func<IRoomHubClient, Task> publish,
        CancellationToken cancellationToken = default)
    {
        ValidateRoom(roomId);
        ArgumentNullException.ThrowIfNull(publish);

        var roomLock = GetRoomLock(roomId);
        var acquired = false;

        try
        {
            await roomLock.WaitAsync(CancellationToken.None);
            acquired = true;

            // Publish before Registry is cleared.
            // Failed delivery must not prevent cleanup.
            try
            {
                await PublishRoomEventCoreAsync(
                    roomId,
                    publish);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Failed to publish RoomClosed for room {RoomId}. "
                    + "Subscriptions will still be revoked.",
                    roomId);
            }

            await RevokeRoomCoreAsync(roomId);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to revoke room {RoomId} after closure.",
                roomId);
        }
        finally
        {
            if (acquired)
            {
                roomLock.Release();
            }
        }
    }

    private async Task PublishRoomEventCoreAsync(
        Guid roomId,
        Func<IRoomHubClient, Task> publish)
    {
        // Caller must hold the room lock.
        // SignalR groups are not an authorization mechanism.
        var connectionIds = _registry.GetConnectionIds(roomId);

        if (connectionIds.Count == 0)
        {
            return;
        }

        await publish(
            _hubContext.Clients.Clients(connectionIds));
    }

    private async Task RevokeMemberCoreAsync(
        Guid roomId,
        Guid guestId)
    {
        // Registry exclusion happens before group cleanup.
        var revocation = _registry.RevokeGuest(
            roomId,
            guestId);

        if (revocation.ConnectionIds.Count == 0)
        {
            return;
        }

        var groupName = GetGroupName(roomId);

        foreach (var connectionId in revocation.ConnectionIds)
        {
            await TryRemoveFromGroupAsync(
                connectionId,
                groupName);
        }

        var payload = new RoomSubscriptionRevoked(
            roomId,
            RoomSubscriptionRevoked.MembershipEnded);

        await NotifyRevokedConnectionsAsync(
            revocation.ConnectionIds,
            payload);

        if (revocation.OnlineGuestsChanged)
        {
            await PublishPresenceChangedAsync(
                revocation.Snapshot);
        }
    }

    private async Task RevokeRoomCoreAsync(
        Guid roomId)
    {
        var connectionIds = _registry.ClearRoom(roomId);

        if (connectionIds.Count == 0)
        {
            return;
        }

        var groupName = GetGroupName(roomId);

        foreach (var connectionId in connectionIds)
        {
            await TryRemoveFromGroupAsync(
                connectionId,
                groupName);
        }

        var payload = new RoomSubscriptionRevoked(
            roomId,
            RoomSubscriptionRevoked.RoomClosed);

        await NotifyRevokedConnectionsAsync(
            connectionIds,
            payload);
    }

    private async Task PublishPresenceChangedAsync(
        RoomPresenceSnapshot snapshot)
    {
        // Never send private presence information directly
        // to SignalR groups. A stale group may still contain
        // a connection whose membership has been revoked.
        //
        // Registry provides the current recipient allowlist.
        var connectionIds = _registry.GetConnectionIds(
            snapshot.RoomId);

        if (connectionIds.Count == 0)
        {
            return;
        }

        try
        {
            await _hubContext.Clients
                .Clients(connectionIds)
                .PresenceChanged(snapshot);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to publish presence for room {RoomId}.",
                snapshot.RoomId);
        }
    }

    private async Task NotifyRevokedConnectionsAsync(
        IReadOnlyList<string> connectionIds,
        RoomSubscriptionRevoked payload)
    {
        foreach (var connectionId in connectionIds)
        {
            try
            {
                await _hubContext.Clients
                    .Client(connectionId)
                    .RoomSubscriptionRevoked(payload);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Failed to deliver subscription revocation "
                    + "for room {RoomId}.",
                    payload.RoomId);
            }
        }
    }

    private async Task<bool> TryRemoveFromGroupAsync(
        string connectionId,
        string groupName)
    {
        try
        {
            await _hubContext.Groups.RemoveFromGroupAsync(
                connectionId,
                groupName,
                CancellationToken.None);

            return true;
        }
        catch (Exception exception)
        {
            // An unsuccessful physical group removal does not
            // restore authorization: the Registry allowlist
            // has already been updated.
            _logger.LogError(
                exception,
                "SignalR group removal failed for {GroupName}. "
                + "The group may contain a stale connection.",
                groupName);

            return false;
        }
    }

    private RoomPresenceSnapshot CreateSnapshot(Guid roomId)
    {
        return new RoomPresenceSnapshot(
            roomId,
            _registry.GetOnlineGuests(roomId));
    }

    private SemaphoreSlim GetRoomLock(Guid roomId)
    {
        var index = (roomId.GetHashCode() & int.MaxValue)
            % LockStripeCount;

        return _roomLocks[index];
    }

    private SemaphoreSlim GetConnectionLock(
        string connectionId)
    {
        var index = (
            StringComparer.Ordinal.GetHashCode(connectionId)
            & int.MaxValue) % LockStripeCount;

        return _connectionLocks[index];
    }

    private static SemaphoreSlim[] CreateLocks()
    {
        return Enumerable
            .Range(0, LockStripeCount)
            .Select(_ => new SemaphoreSlim(1, 1))
            .ToArray();
    }

    public static string GetGroupName(Guid roomId)
    {
        ValidateRoom(roomId);

        return $"room:{roomId:N}";
    }

    private static void ValidateRoom(Guid roomId)
    {
        if (roomId == Guid.Empty)
        {
            throw new HubException("invalidRoomId");
        }
    }

    private static void ValidateGuest(Guid guestId)
    {
        if (guestId == Guid.Empty)
        {
            throw new HubException("invalidGuest");
        }
    }

    private static void ValidateConnection(string connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId))
        {
            throw new HubException("invalidConnection");
        }
    }

    public void Dispose()
    {
        foreach (var semaphore in _roomLocks)
        {
            semaphore.Dispose();
        }

        foreach (var semaphore in _connectionLocks)
        {
            semaphore.Dispose();
        }
    }
}