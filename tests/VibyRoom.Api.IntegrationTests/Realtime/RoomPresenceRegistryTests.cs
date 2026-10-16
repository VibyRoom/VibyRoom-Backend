using System.Collections.Concurrent;
using VibyRoom.Api.Realtime;
using Xunit;

namespace VibyRoom.Api.IntegrationTests.Realtime;

public sealed class RoomPresenceRegistryTests
{
    // Registration

    [Fact]
    public void Register_FirstConnection_AddsGuestOnline()
    {
        var registry = new RoomPresenceRegistry();
        var roomId = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        var result = registry.Register(
            roomId, guestId, "connection-1");

        Assert.True(result.SubscriptionChanged);
        Assert.True(result.OnlineGuestsChanged);
        Assert.Equal(roomId, result.RoomId);
        Assert.Equal(roomId, result.Snapshot.RoomId);
        Assert.Equal(
            new[] { guestId },
            result.Snapshot.OnlineGuestIds);
        Assert.Contains(
            roomId,
            registry.GetRoomsForConnection("connection-1"));
    }

    [Fact]
    public void Register_SameConnectionTwice_IsIdempotent()
    {
        var registry = new RoomPresenceRegistry();
        var roomId = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        registry.Register(roomId, guestId, "connection-1");

        var result = registry.Register(
            roomId, guestId, "connection-1");

        Assert.False(result.SubscriptionChanged);
        Assert.False(result.OnlineGuestsChanged);
        Assert.Single(registry.GetOnlineGuests(roomId));
        Assert.Single(
            registry.GetRoomsForConnection("connection-1"));
    }

    [Fact]
    public void Register_TwoTabsOfSameGuest_CountsOnce()
    {
        var registry = new RoomPresenceRegistry();
        var roomId = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        var first = registry.Register(
            roomId, guestId, "tab-1");

        var second = registry.Register(
            roomId, guestId, "tab-2");

        Assert.True(first.OnlineGuestsChanged);
        Assert.False(second.OnlineGuestsChanged);
        Assert.True(second.SubscriptionChanged);
        Assert.Equal(
            new[] { guestId },
            registry.GetOnlineGuests(roomId));
    }

    [Fact]
    public void Register_DifferentGuests_AreCountedSeparately()
    {
        var registry = new RoomPresenceRegistry();
        var roomId = Guid.NewGuid();
        var firstGuest = Guid.NewGuid();
        var secondGuest = Guid.NewGuid();

        registry.Register(roomId, firstGuest, "connection-1");

        var second = registry.Register(
            roomId, secondGuest, "connection-2");

        Assert.True(second.OnlineGuestsChanged);
        Assert.Equal(2, second.Snapshot.OnlineGuestIds.Count);
        Assert.Contains(firstGuest, registry.GetOnlineGuests(roomId));
        Assert.Contains(secondGuest, registry.GetOnlineGuests(roomId));
    }

    [Fact]
    public void Register_SameConnectionInTwoRooms_IsSupported()
    {
        var registry = new RoomPresenceRegistry();
        var firstRoom = Guid.NewGuid();
        var secondRoom = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        registry.Register(firstRoom, guestId, "connection-1");
        registry.Register(secondRoom, guestId, "connection-1");

        var rooms = registry.GetRoomsForConnection(
            "connection-1");

        Assert.Equal(2, rooms.Count);
        Assert.Contains(firstRoom, rooms);
        Assert.Contains(secondRoom, rooms);
        Assert.Contains(guestId, registry.GetOnlineGuests(firstRoom));
        Assert.Contains(guestId, registry.GetOnlineGuests(secondRoom));
    }

    // Unregister

    [Fact]
    public void Unregister_OneOfTwoTabs_GuestRemainsOnline()
    {
        var registry = new RoomPresenceRegistry();
        var roomId = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        registry.Register(roomId, guestId, "tab-1");
        registry.Register(roomId, guestId, "tab-2");

        var result = registry.Unregister(roomId, "tab-1");

        Assert.True(result.SubscriptionChanged);
        Assert.False(result.OnlineGuestsChanged);
        Assert.Contains(guestId, registry.GetOnlineGuests(roomId));
        Assert.Empty(registry.GetRoomsForConnection("tab-1"));
        Assert.Contains(
            roomId,
            registry.GetRoomsForConnection("tab-2"));
    }

    [Fact]
    public void Unregister_LastTab_GuestBecomesOffline()
    {
        var registry = new RoomPresenceRegistry();
        var roomId = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        registry.Register(roomId, guestId, "tab-1");
        registry.Register(roomId, guestId, "tab-2");

        registry.Unregister(roomId, "tab-1");

        var result = registry.Unregister(roomId, "tab-2");

        Assert.True(result.SubscriptionChanged);
        Assert.True(result.OnlineGuestsChanged);
        Assert.Empty(result.Snapshot.OnlineGuestIds);
        Assert.Empty(registry.GetOnlineGuests(roomId));
    }

    [Fact]
    public void Unregister_OneRoom_DoesNotAffectAnother()
    {
        var registry = new RoomPresenceRegistry();
        var firstRoom = Guid.NewGuid();
        var secondRoom = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        registry.Register(firstRoom, guestId, "connection-1");
        registry.Register(secondRoom, guestId, "connection-1");

        var result = registry.Unregister(
            firstRoom, "connection-1");

        Assert.Equal(firstRoom, result.RoomId);
        Assert.True(result.OnlineGuestsChanged);
        Assert.Empty(registry.GetOnlineGuests(firstRoom));
        Assert.Contains(
            guestId,
            registry.GetOnlineGuests(secondRoom));
        Assert.Equal(
            new[] { secondRoom },
            registry.GetRoomsForConnection("connection-1"));
    }

    [Fact]
    public void Unregister_UnknownSubscription_IsSafe()
    {
        var registry = new RoomPresenceRegistry();
        var roomId = Guid.NewGuid();

        var result = registry.Unregister(
            roomId, "unknown-connection");

        Assert.False(result.SubscriptionChanged);
        Assert.False(result.OnlineGuestsChanged);
        Assert.Equal(roomId, result.RoomId);
        Assert.Empty(result.Snapshot.OnlineGuestIds);
    }

    // Connection cleanup

    [Fact]
    public void UnregisterConnection_RemovesAllRoomSubscriptions()
    {
        var registry = new RoomPresenceRegistry();
        var firstRoom = Guid.NewGuid();
        var secondRoom = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        registry.Register(firstRoom, guestId, "connection-1");
        registry.Register(secondRoom, guestId, "connection-1");

        var results = registry.UnregisterConnection(
            "connection-1");

        Assert.Equal(2, results.Count);
        Assert.Contains(firstRoom, results.Select(x => x.RoomId));
        Assert.Contains(secondRoom, results.Select(x => x.RoomId));

        Assert.All(results, result =>
        {
            Assert.True(result.SubscriptionChanged);
            Assert.True(result.OnlineGuestsChanged);
            Assert.Empty(result.Snapshot.OnlineGuestIds);
        });

        Assert.Empty(registry.GetRoomsForConnection("connection-1"));
        Assert.Empty(registry.GetOnlineGuests(firstRoom));
        Assert.Empty(registry.GetOnlineGuests(secondRoom));
    }

    [Fact]
    public void UnregisterConnection_OnlyRemovesSpecifiedTab()
    {
        var registry = new RoomPresenceRegistry();
        var roomId = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        registry.Register(roomId, guestId, "tab-1");
        registry.Register(roomId, guestId, "tab-2");

        var results = registry.UnregisterConnection("tab-1");

        var result = Assert.Single(results);

        Assert.True(result.SubscriptionChanged);
        Assert.False(result.OnlineGuestsChanged);
        Assert.Equal(roomId, result.RoomId);

        Assert.Contains(guestId, registry.GetOnlineGuests(roomId));
        Assert.Contains(
            roomId,
            registry.GetRoomsForConnection("tab-2"));
    }

    [Fact]
    public void UnregisterConnection_UnknownConnection_ReturnsEmpty()
    {
        var registry = new RoomPresenceRegistry();

        var results = registry.UnregisterConnection("unknown");

        Assert.Empty(results);
    }

    // Guest revocation

    [Fact]
    public void RevokeGuest_RemovesAllGuestConnections()
    {
        var registry = new RoomPresenceRegistry();
        var roomId = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        registry.Register(roomId, guestId, "tab-1");
        registry.Register(roomId, guestId, "tab-2");
        registry.Register(roomId, guestId, "tab-3");

        var result = registry.RevokeGuest(roomId, guestId);

        Assert.True(result.OnlineGuestsChanged);
        Assert.Equal(3, result.ConnectionIds.Count);
        Assert.Contains("tab-1", result.ConnectionIds);
        Assert.Contains("tab-2", result.ConnectionIds);
        Assert.Contains("tab-3", result.ConnectionIds);

        Assert.Empty(registry.GetOnlineGuests(roomId));
        Assert.Empty(registry.GetRoomsForConnection("tab-1"));
        Assert.Empty(registry.GetRoomsForConnection("tab-2"));
        Assert.Empty(registry.GetRoomsForConnection("tab-3"));
    }

    [Fact]
    public void RevokeGuest_PreservesOtherGuestsAndRooms()
    {
        var registry = new RoomPresenceRegistry();
        var firstRoom = Guid.NewGuid();
        var secondRoom = Guid.NewGuid();
        var revokedGuest = Guid.NewGuid();
        var remainingGuest = Guid.NewGuid();

        registry.Register(
            firstRoom, revokedGuest, "revoked-connection");

        registry.Register(
            firstRoom, remainingGuest, "remaining-connection");

        registry.Register(
            secondRoom, revokedGuest, "revoked-connection");

        var result = registry.RevokeGuest(
            firstRoom, revokedGuest);

        Assert.True(result.OnlineGuestsChanged);
        Assert.Equal(
            new[] { remainingGuest },
            registry.GetOnlineGuests(firstRoom));

        Assert.Contains(
            revokedGuest,
            registry.GetOnlineGuests(secondRoom));

        Assert.Equal(
            new[] { secondRoom },
            registry.GetRoomsForConnection("revoked-connection"));

        Assert.Contains(
            firstRoom,
            registry.GetRoomsForConnection("remaining-connection"));
    }

    [Fact]
    public void RevokeGuest_AbsentGuest_IsIdempotent()
    {
        var registry = new RoomPresenceRegistry();
        var roomId = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        var first = registry.RevokeGuest(roomId, guestId);
        var second = registry.RevokeGuest(roomId, guestId);

        Assert.False(first.OnlineGuestsChanged);
        Assert.False(second.OnlineGuestsChanged);
        Assert.Empty(first.ConnectionIds);
        Assert.Empty(second.ConnectionIds);
        Assert.Empty(registry.GetOnlineGuests(roomId));
    }

    // Room cleanup

    [Fact]
    public void ClearRoom_RemovesEveryConnection()
    {
        var registry = new RoomPresenceRegistry();
        var roomId = Guid.NewGuid();

        registry.Register(
            roomId, Guid.NewGuid(), "connection-1");

        registry.Register(
            roomId, Guid.NewGuid(), "connection-2");

        registry.Register(
            roomId, Guid.NewGuid(), "connection-3");

        var removed = registry.ClearRoom(roomId);

        Assert.Equal(3, removed.Count);
        Assert.Contains("connection-1", removed);
        Assert.Contains("connection-2", removed);
        Assert.Contains("connection-3", removed);

        Assert.Empty(registry.GetOnlineGuests(roomId));

        foreach (var connectionId in removed)
        {
            Assert.Empty(
                registry.GetRoomsForConnection(connectionId));
        }
    }

    [Fact]
    public void ClearRoom_DoesNotAffectAnotherRoom()
    {
        var registry = new RoomPresenceRegistry();
        var firstRoom = Guid.NewGuid();
        var secondRoom = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        registry.Register(firstRoom, guestId, "connection-1");
        registry.Register(secondRoom, guestId, "connection-1");

        registry.ClearRoom(firstRoom);

        Assert.Empty(registry.GetOnlineGuests(firstRoom));
        Assert.Contains(
            guestId,
            registry.GetOnlineGuests(secondRoom));

        Assert.Equal(
            new[] { secondRoom },
            registry.GetRoomsForConnection("connection-1"));
    }

    [Fact]
    public void ClearRoom_RepeatedCall_IsSafe()
    {
        var registry = new RoomPresenceRegistry();
        var roomId = Guid.NewGuid();

        registry.Register(
            roomId, Guid.NewGuid(), "connection-1");

        var first = registry.ClearRoom(roomId);
        var second = registry.ClearRoom(roomId);

        Assert.Single(first);
        Assert.Empty(second);
        Assert.Empty(registry.GetOnlineGuests(roomId));
    }

    // Snapshots and immutability

    [Fact]
    public void GetOnlineGuests_ReturnsUniqueSortedGuestIds()
    {
        var registry = new RoomPresenceRegistry();
        var roomId = Guid.NewGuid();

        var guests = Enumerable.Range(0, 20)
            .Select(_ => Guid.NewGuid())
            .ToArray();

        for (var index = 0; index < guests.Length; index++)
        {
            registry.Register(
                roomId,
                guests[index],
                $"connection-{index}");
        }

        registry.Register(roomId, guests[0], "additional-tab");

        var online = registry.GetOnlineGuests(roomId);

        Assert.Equal(guests.Length, online.Count);
        Assert.Equal(online.Count, online.Distinct().Count());
        Assert.Equal(online.Order().ToArray(), online);
    }

    [Fact]
    public void MutationSnapshot_DoesNotChangeAfterRegistryMutation()
    {
        var registry = new RoomPresenceRegistry();
        var roomId = Guid.NewGuid();
        var firstGuest = Guid.NewGuid();
        var secondGuest = Guid.NewGuid();

        var first = registry.Register(
            roomId, firstGuest, "connection-1");

        registry.Register(
            roomId, secondGuest, "connection-2");

        Assert.Equal(
            new[] { firstGuest },
            first.Snapshot.OnlineGuestIds);

        Assert.Equal(2, registry.GetOnlineGuests(roomId).Count);
    }

    [Fact]
    public void GetOnlineGuests_ReturnsReadOnlySnapshot()
    {
        var registry = new RoomPresenceRegistry();
        var roomId = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        registry.Register(roomId, guestId, "connection-1");

        var snapshot = registry.GetOnlineGuests(roomId);

        var mutable = Assert.IsAssignableFrom<IList<Guid>>(
            snapshot);

        Assert.Throws<NotSupportedException>(
            () => mutable[0] = Guid.NewGuid());

        Assert.Equal(
            new[] { guestId },
            registry.GetOnlineGuests(roomId));
    }

    [Fact]
    public void GetRoomsForConnection_ReturnsReadOnlySnapshot()
    {
        var registry = new RoomPresenceRegistry();
        var roomId = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        registry.Register(roomId, guestId, "connection-1");

        var rooms = registry.GetRoomsForConnection(
            "connection-1");

        var mutable = Assert.IsAssignableFrom<IList<Guid>>(
            rooms);

        Assert.Throws<NotSupportedException>(
            () => mutable[0] = Guid.NewGuid());

        Assert.Equal(
            new[] { roomId },
            registry.GetRoomsForConnection("connection-1"));
    }

    [Fact]
    public void RevokeGuest_ReturnsReadOnlyConnectionIds()
    {
        var registry = new RoomPresenceRegistry();
        var roomId = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        registry.Register(roomId, guestId, "connection-1");

        var result = registry.RevokeGuest(roomId, guestId);

        var mutable = Assert.IsAssignableFrom<IList<string>>(
            result.ConnectionIds);

        Assert.Throws<NotSupportedException>(
            () => mutable[0] = "modified");

        Assert.Equal(
            new[] { "connection-1" },
            result.ConnectionIds);
    }

    // Validation

    [Fact]
    public void Register_ConnectionBelongingToAnotherGuest_Throws()
    {
        var registry = new RoomPresenceRegistry();
        var roomId = Guid.NewGuid();

        registry.Register(
            roomId, Guid.NewGuid(), "connection-1");

        Assert.Throws<InvalidOperationException>(() =>
            registry.Register(
                roomId,
                Guid.NewGuid(),
                "connection-1"));

        Assert.Single(registry.GetOnlineGuests(roomId));
    }

    [Fact]
    public void InvalidIdentifiers_AreRejected()
    {
        var registry = new RoomPresenceRegistry();
        var roomId = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() =>
            registry.Register(
                Guid.Empty, guestId, "connection-1"));

        Assert.Throws<ArgumentException>(() =>
            registry.Register(
                roomId, Guid.Empty, "connection-1"));

        Assert.Throws<ArgumentException>(() =>
            registry.Register(
                roomId, guestId, ""));

        Assert.Throws<ArgumentException>(() =>
            registry.Unregister(Guid.Empty, "connection-1"));

        Assert.Throws<ArgumentException>(() =>
            registry.RevokeGuest(roomId, Guid.Empty));

        Assert.Throws<ArgumentException>(() =>
            registry.ClearRoom(Guid.Empty));
    }

    // Concurrency

    [Fact]
    public void ConcurrentRegister_DifferentConnections_RemainsConsistent()
    {
        const int connectionCount = 500;

        var registry = new RoomPresenceRegistry();
        var roomId = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        var results = new ConcurrentBag<RoomPresenceMutation>();

        Parallel.For(0, connectionCount, index =>
        {
            var result = registry.Register(
                roomId,
                guestId,
                $"connection-{index}");

            results.Add(result);
        });

        Assert.Equal(connectionCount, results.Count);
        Assert.All(results, result =>
            Assert.True(result.SubscriptionChanged));

        Assert.Equal(
            1,
            results.Count(result => result.OnlineGuestsChanged));

        Assert.Equal(
            new[] { guestId },
            registry.GetOnlineGuests(roomId));

        for (var index = 0; index < connectionCount; index++)
        {
            Assert.Contains(
                roomId,
                registry.GetRoomsForConnection(
                    $"connection-{index}"));
        }
    }

    [Fact]
    public void ConcurrentRegister_SameConnection_DoesNotDuplicate()
    {
        const int attemptCount = 500;

        var registry = new RoomPresenceRegistry();
        var roomId = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        var results = new ConcurrentBag<RoomPresenceMutation>();

        Parallel.For(0, attemptCount, _ =>
        {
            results.Add(registry.Register(
                roomId, guestId, "shared-connection"));
        });

        Assert.Equal(
            1,
            results.Count(result => result.SubscriptionChanged));

        Assert.Equal(
            1,
            results.Count(result => result.OnlineGuestsChanged));

        Assert.Equal(
            new[] { guestId },
            registry.GetOnlineGuests(roomId));

        var removed = registry.UnregisterConnection(
            "shared-connection");

        Assert.Single(removed);
        Assert.Empty(registry.GetOnlineGuests(roomId));
    }

    [Fact]
    public void ConcurrentUnregister_AllConnections_OneOfflineTransition()
    {
        const int connectionCount = 500;

        var registry = new RoomPresenceRegistry();
        var roomId = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        for (var index = 0; index < connectionCount; index++)
        {
            registry.Register(
                roomId, guestId, $"connection-{index}");
        }

        var results = new ConcurrentBag<RoomPresenceMutation>();

        Parallel.For(0, connectionCount, index =>
        {
            var result = registry.Unregister(
                roomId,
                $"connection-{index}");

            results.Add(result);
        });

        Assert.Equal(connectionCount, results.Count);

        Assert.All(results, result =>
            Assert.True(result.SubscriptionChanged));

        Assert.Equal(
            1,
            results.Count(result => result.OnlineGuestsChanged));

        Assert.Empty(registry.GetOnlineGuests(roomId));

        for (var index = 0; index < connectionCount; index++)
        {
            Assert.Empty(registry.GetRoomsForConnection(
                $"connection-{index}"));
        }
    }

    [Fact]
    public void ConcurrentRegisterAndRevokeGuest_PreservesIndexes()
    {
        const int iterationCount = 500;

        var registry = new RoomPresenceRegistry();
        var roomId = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        Parallel.For(0, iterationCount, index =>
        {
            if (index % 2 == 0)
            {
                registry.RevokeGuest(roomId, guestId);
            }
            else
            {
                registry.Register(
                    roomId,
                    guestId,
                    $"connection-{index}");
            }
        });

        var registeredConnections = Enumerable
            .Range(0, iterationCount)
            .Where(index => index % 2 != 0)
            .Select(index => $"connection-{index}")
            .Where(connectionId =>
                registry.GetRoomsForConnection(connectionId)
                    .Contains(roomId))
            .ToArray();

        var online = registry.GetOnlineGuests(roomId);

        Assert.Equal(
            registeredConnections.Length > 0,
            online.Contains(guestId));

        Assert.True(online.Count <= 1);

        // A final revocation must clean all surviving registrations.
        var finalRevocation = registry.RevokeGuest(
            roomId, guestId);

        Assert.Equal(
            registeredConnections.Length,
            finalRevocation.ConnectionIds.Count);

        Assert.Empty(registry.GetOnlineGuests(roomId));

        for (var index = 1; index < iterationCount; index += 2)
        {
            Assert.Empty(
                registry.GetRoomsForConnection(
                    $"connection-{index}"));
        }
    }
}