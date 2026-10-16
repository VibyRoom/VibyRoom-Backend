using VibyRoom.Domain.Core.Entities.Rooms.Enums;
using VibyRoom.Domain.Music.Queue;
using VibyRoom.Domain.Music.Queue.Exceptions;
using VibyRoom.Domain.Music.Tracks;
using Xunit;

namespace VibyRoom.Api.IntegrationTests.Music.Queue;

public sealed class RoomMusicSessionTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();
    private static readonly Guid OtherMemberId = Guid.NewGuid();

    [Fact]
    public void Create_InitializesEmptyQueue()
    {
        var roomId = Guid.NewGuid();

        var session = RoomMusicSession.Create(roomId, 100);

        Assert.Equal(roomId, session.RoomId);
        Assert.Equal(0, session.Version);
        Assert.Equal(100, session.MaxPendingItems);
        Assert.Empty(session.PendingItems);
    }

    [Fact]
    public void Create_EmptyRoomId_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => RoomMusicSession.Create(Guid.Empty, 100));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_InvalidLimit_Throws(int limit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RoomMusicSession.Create(
                Guid.NewGuid(),
                limit));
    }

    [Fact]
    public void AddTrack_AddsItemAtEndAndIncrementsVersion()
    {
        var session = CreateSession();
        var track = CreateTrack();

        var item = session.AddTrack(
            track,
            MemberId,
            DateTimeOffset.UtcNow);

        Assert.NotEqual(Guid.Empty, item.Id);
        Assert.Same(track, item.Track);
        Assert.Equal(MemberId, item.AddedByGuestId);
        Assert.Equal(0, item.Position);
        Assert.Equal(1, session.Version);

        Assert.Same(item, Assert.Single(session.PendingItems));
    }

    [Fact]
    public void AddTrack_MultipleItems_HaveSequentialPositions()
    {
        var session = CreateSession();

        Add(session, "first");
        Add(session, "second");
        Add(session, "third");

        Assert.Equal(
            new[] { 0, 1, 2 },
            session.PendingItems
                .Select(item => item.Position)
                .ToArray());

        Assert.Equal(3, session.Version);
    }

    [Fact]
    public void AddTrack_AllowsDuplicateReferences()
    {
        var session = CreateSession();
        var track = CreateTrack();

        var first = session.AddTrack(
            track,
            MemberId,
            DateTimeOffset.UtcNow);

        var second = session.AddTrack(
            track,
            MemberId,
            DateTimeOffset.UtcNow);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(
            first.Track.Reference,
            second.Track.Reference);

        Assert.Equal(2, session.PendingItems.Count);
    }

    [Fact]
    public void AddTrack_QueueFull_DoesNotChangeState()
    {
        var session = CreateSession(maxPendingItems: 1);

        Add(session, "first");

        var version = session.Version;
        var item = Assert.Single(session.PendingItems);

        var exception =
            Assert.Throws<RoomQueueDomainException>(
                () => Add(session, "second"));

        Assert.Equal(
            RoomQueueDomainErrorCode.QueueFull,
            exception.ErrorCode);

        Assert.Equal(version, session.Version);
        Assert.Same(item, Assert.Single(session.PendingItems));
    }

    [Fact]
    public void PendingItems_CannotBeModifiedExternally()
    {
        var session = CreateSession();

        Add(session, "first");

        var items = Assert.IsAssignableFrom<IList<QueueItem>>(
            session.PendingItems);

        Assert.Throws<NotSupportedException>(
            () => items.Clear());

        Assert.Single(session.PendingItems);
        Assert.Equal(1, session.Version);
    }

    [Fact]
    public void RemoveTrack_MemberCanRemoveOwnItem()
    {
        var session = CreateSession();

        var ownItem = Add(
            session,
            "first",
            MemberId);

        Add(session, "second", OtherMemberId);

        session.RemoveTrack(
            ownItem.Id,
            MemberId,
            RoomRole.Member);

        Assert.Single(session.PendingItems);
        Assert.Equal(0, session.PendingItems[0].Position);
        Assert.Equal(3, session.Version);
    }

    [Fact]
    public void RemoveTrack_MemberCannotRemoveAnotherMembersItem()
    {
        var session = CreateSession();

        var item = Add(
            session,
            "first",
            OtherMemberId);

        var version = session.Version;

        var exception =
            Assert.Throws<RoomQueueDomainException>(
                () => session.RemoveTrack(
                    item.Id,
                    MemberId,
                    RoomRole.Member));

        Assert.Equal(
            RoomQueueDomainErrorCode.AccessDenied,
            exception.ErrorCode);

        Assert.Equal(version, session.Version);
        Assert.Same(item, Assert.Single(session.PendingItems));
    }

    [Fact]
    public void RemoveTrack_OwnerCanRemoveAnyItem()
    {
        var session = CreateSession();

        var item = Add(
            session,
            "first",
            OtherMemberId);

        session.RemoveTrack(
            item.Id,
            OwnerId,
            RoomRole.Owner);

        Assert.Empty(session.PendingItems);
        Assert.Equal(2, session.Version);
    }

    [Fact]
    public void RemoveTrack_MissingItem_ThrowsWithoutMutation()
    {
        var session = CreateSession();

        Add(session, "first");

        var version = session.Version;

        var exception =
            Assert.Throws<RoomQueueDomainException>(
                () => session.RemoveTrack(
                    Guid.NewGuid(),
                    OwnerId,
                    RoomRole.Owner));

        Assert.Equal(
            RoomQueueDomainErrorCode.QueueItemNotFound,
            exception.ErrorCode);

        Assert.Equal(version, session.Version);
        Assert.Single(session.PendingItems);
    }

    [Fact]
    public void RemoveTrack_NormalizesRemainingPositions()
    {
        var session = CreateSession();

        Add(session, "first");
        var middle = Add(session, "second");
        Add(session, "third");

        session.RemoveTrack(
            middle.Id,
            OwnerId,
            RoomRole.Owner);

        Assert.Equal(
            new[] { 0, 1 },
            session.PendingItems
                .Select(item => item.Position)
                .ToArray());
    }

    [Fact]
    public void MoveTrack_OwnerCanMoveToBeginning()
    {
        var session = CreateSession();

        Add(session, "first");
        Add(session, "second");
        var last = Add(session, "third");

        var changed = session.MoveTrack(
            last.Id,
            0,
            RoomRole.Owner);

        Assert.True(changed);
        Assert.Equal(4, session.Version);

        Assert.Equal(
            new[] { "third", "first", "second" },
            session.PendingItems
                .Select(item => item.Track.Reference.TrackId)
                .ToArray());

        Assert.Equal(
            new[] { 0, 1, 2 },
            session.PendingItems
                .Select(item => item.Position)
                .ToArray());
    }

    [Fact]
    public void MoveTrack_OwnerCanMoveToEnd()
    {
        var session = CreateSession();

        var first = Add(session, "first");
        Add(session, "second");
        Add(session, "third");

        var changed = session.MoveTrack(
            first.Id,
            2,
            RoomRole.Owner);

        Assert.True(changed);

        Assert.Equal(
            new[] { "second", "third", "first" },
            session.PendingItems
                .Select(item => item.Track.Reference.TrackId)
                .ToArray());

        Assert.Equal(4, session.Version);
    }

    [Fact]
    public void MoveTrack_MemberIsDenied()
    {
        var session = CreateSession();
        var item = Add(session, "first");

        var version = session.Version;

        var exception =
            Assert.Throws<RoomQueueDomainException>(
                () => session.MoveTrack(
                    item.Id,
                    0,
                    RoomRole.Member));

        Assert.Equal(
            RoomQueueDomainErrorCode.AccessDenied,
            exception.ErrorCode);

        Assert.Equal(version, session.Version);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void MoveTrack_InvalidPosition_DoesNotChangeState(
        int position)
    {
        var session = CreateSession();

        var first = Add(session, "first");
        Add(session, "second");

        var version = session.Version;

        var originalIds = session.PendingItems
            .Select(item => item.Id)
            .ToArray();

        var exception =
            Assert.Throws<RoomQueueDomainException>(
                () => session.MoveTrack(
                    first.Id,
                    position,
                    RoomRole.Owner));

        Assert.Equal(
            RoomQueueDomainErrorCode.InvalidQueuePosition,
            exception.ErrorCode);

        Assert.Equal(version, session.Version);

        Assert.Equal(
            originalIds,
            session.PendingItems
                .Select(item => item.Id)
                .ToArray());
    }

    [Fact]
    public void MoveTrack_SamePosition_IsIdempotent()
    {
        var session = CreateSession();

        var item = Add(session, "first");
        Add(session, "second");

        var version = session.Version;

        var changed = session.MoveTrack(
            item.Id,
            0,
            RoomRole.Owner);

        Assert.False(changed);
        Assert.Equal(version, session.Version);
        Assert.Same(item, session.PendingItems[0]);
    }

    [Fact]
    public void MoveTrack_MissingItem_DoesNotChangeState()
    {
        var session = CreateSession();

        Add(session, "first");

        var version = session.Version;

        var exception =
            Assert.Throws<RoomQueueDomainException>(
                () => session.MoveTrack(
                    Guid.NewGuid(),
                    0,
                    RoomRole.Owner));

        Assert.Equal(
            RoomQueueDomainErrorCode.QueueItemNotFound,
            exception.ErrorCode);

        Assert.Equal(version, session.Version);
    }

    [Fact]
    public void FindItem_ReturnsExistingItemOrNull()
    {
        var session = CreateSession();

        var item = Add(session, "first");

        Assert.Same(item, session.FindItem(item.Id));
        Assert.Null(session.FindItem(Guid.NewGuid()));
        Assert.Null(session.FindItem(Guid.Empty));
    }

    [Fact]
    public void AddTrack_ConvertsTimestampToUtc()
    {
        var session = CreateSession();

        var sourceTime = new DateTimeOffset(
            2026, 10, 16, 15, 30, 0,
            TimeSpan.FromHours(3));

        var item = session.AddTrack(
            CreateTrack(),
            MemberId,
            sourceTime);

        Assert.Equal(
            TimeSpan.Zero,
            item.AddedAtUtc.Offset);

        Assert.Equal(
            sourceTime.ToUniversalTime(),
            item.AddedAtUtc);
    }

    private static RoomMusicSession CreateSession(
        int maxPendingItems = 100)
    {
        return RoomMusicSession.Create(
            Guid.NewGuid(),
            maxPendingItems);
    }

    private static QueueItem Add(
        RoomMusicSession session,
        string trackId,
        Guid? guestId = null)
    {
        return session.AddTrack(
            CreateTrack(trackId),
            guestId ?? MemberId,
            DateTimeOffset.UtcNow);
    }

    private static QueuedTrack CreateTrack(
        string trackId = "D7KyD")
    {
        return new QueuedTrack(
            new TrackReference("audius", trackId),
            "Test Track",
            new[] { "Test Artist" },
            180000,
            "https://example.com/artwork.jpg",
            "https://audius.co/artist/track");
    }
}