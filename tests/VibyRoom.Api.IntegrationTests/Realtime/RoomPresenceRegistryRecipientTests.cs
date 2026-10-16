using VibyRoom.Api.Realtime;
using Xunit;

namespace VibyRoom.Api.IntegrationTests.Realtime;

public sealed class RoomPresenceRegistryRecipientTests
{
    [Fact]
    public void GetConnectionIds_ReturnsAllTabs()
    {
        var registry = new RoomPresenceRegistry();
        var roomId = Guid.NewGuid();
        var guestId = Guid.NewGuid();

        registry.Register(roomId, guestId, "tab-1");
        registry.Register(roomId, guestId, "tab-2");

        Assert.Equal(
            new[] { "tab-1", "tab-2" },
            registry.GetConnectionIds(roomId));
    }

    [Fact]
    public void GetConnectionIds_AfterRevocation_ExcludesGuest()
    {
        var registry = new RoomPresenceRegistry();
        var roomId = Guid.NewGuid();
        var firstGuest = Guid.NewGuid();
        var secondGuest = Guid.NewGuid();

        registry.Register(roomId, firstGuest, "connection-1");
        registry.Register(roomId, secondGuest, "connection-2");

        registry.RevokeGuest(roomId, firstGuest);

        Assert.Equal(
            new[] { "connection-2" },
            registry.GetConnectionIds(roomId));
    }

    [Fact]
    public void GetConnectionIds_ReturnsReadOnlySnapshot()
    {
        var registry = new RoomPresenceRegistry();
        var roomId = Guid.NewGuid();

        registry.Register(
            roomId, Guid.NewGuid(), "connection-1");

        var snapshot = registry.GetConnectionIds(roomId);

        var mutable =
            Assert.IsAssignableFrom<IList<string>>(snapshot);

        Assert.Throws<NotSupportedException>(
            () => mutable[0] = "modified");

        registry.ClearRoom(roomId);

        Assert.Equal(
            new[] { "connection-1" },
            snapshot);

        Assert.Empty(registry.GetConnectionIds(roomId));
    }
}