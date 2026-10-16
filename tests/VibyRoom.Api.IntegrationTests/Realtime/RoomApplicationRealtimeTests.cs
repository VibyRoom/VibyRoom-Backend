using Microsoft.Extensions.Logging.Abstractions;
using VibyRoom.Application.Abstractions.Authentication;
using VibyRoom.Application.Abstractions.Exceptions;
using VibyRoom.Application.Abstractions.Invitations;
using VibyRoom.Application.Abstractions.Repositories;
using VibyRoom.Application.Abstractions.Realtime;
using VibyRoom.Application.Configuration;
using VibyRoom.Application.Contracts.Queue;
using VibyRoom.Application.Contracts.Rooms;
using VibyRoom.Application.Errors;
using VibyRoom.Application.Rooms;
using VibyRoom.Domain.Core.Entities.Rooms;
using VibyRoom.Domain.Core.Entities.Rooms.Enums;
using Xunit;

namespace VibyRoom.Api.IntegrationTests.Realtime;

public sealed class RoomApplicationRealtimeTests
{
    [Fact]
    public async Task JoinMember_SavesBeforePublishing()
    {
        var fixture = new Fixture(joinMember: false);
        fixture.Guest.GuestId = fixture.MemberId;

        var result = await fixture.Service.JoinRoomAsync(
            new JoinRoomCommand(
                fixture.Room.InviteCode,
                "New member"),
            TestContext.Current.CancellationToken);

        Assert.True(result.Joined);

        Assert.Equal(
            new[] { "save", "member-joined" },
            fixture.Events);

        Assert.Equal(
            fixture.Room.Id,
            fixture.Publisher.LastRoomId);

        var member = Assert.Single(
            fixture.Room.Members,
            x => x.GuestId == fixture.MemberId);

        Assert.Equal(
            new RoomMemberRealtimeData(
                member.GuestId,
                member.DisplayName,
                member.JoinedAtUtc),
            fixture.Publisher.LastJoinedMember);

        Assert.Equal("New member", member.DisplayName);
    }

    [Fact]
    public async Task RepeatedJoin_DoesNotSaveOrPublish()
    {
        var fixture = new Fixture();
        fixture.Guest.GuestId = fixture.MemberId;

        var result = await fixture.Service.JoinRoomAsync(
            new JoinRoomCommand(
                fixture.Room.InviteCode,
                "Different name"),
            TestContext.Current.CancellationToken);

        Assert.False(result.Joined);
        Assert.Empty(fixture.Events);
        Assert.Null(fixture.Publisher.LastJoinedMember);

        var member = Assert.Single(
            fixture.Room.Members,
            x => x.GuestId == fixture.MemberId);

        Assert.Equal("Member", member.DisplayName);
    }

    [Fact]
    public async Task LeaveMember_SavesBeforePublishing()
    {
        var fixture = new Fixture();
        fixture.Guest.GuestId = fixture.MemberId;

        await fixture.Service.LeaveRoomAsync(
            fixture.Room.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            new[] { "save", "member-left" },
            fixture.Events);

        Assert.Equal(
            fixture.MemberId,
            fixture.Publisher.LastGuestId);

        Assert.Null(fixture.Room.GetRole(fixture.MemberId));
        Assert.Equal(RoomStatus.Active, fixture.Room.Status);
    }

    [Fact]
    public async Task LeaveSoleOwner_PublishesOnlyRoomClosed()
    {
        var fixture = new Fixture(joinMember: false);

        await fixture.Service.LeaveRoomAsync(
            fixture.Room.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(RoomStatus.Closed, fixture.Room.Status);

        Assert.Equal(
            new[] { "save", "room-closed" },
            fixture.Events);

        Assert.Equal(
            fixture.Room.Id,
            fixture.Publisher.LastRoomId);
    }

    [Fact]
    public async Task RemoveMember_SavesBeforePublishing()
    {
        var fixture = new Fixture();

        await fixture.Service.RemoveMemberAsync(
            fixture.Room.Id,
            fixture.MemberId,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            new[] { "save", "member-removed" },
            fixture.Events);

        Assert.Equal(
            fixture.MemberId,
            fixture.Publisher.LastGuestId);

        Assert.Null(fixture.Room.GetRole(fixture.MemberId));
    }

    [Fact]
    public async Task TransferOwnership_PublishesBothOwnerIds()
    {
        var fixture = new Fixture();

        await fixture.Service.TransferOwnershipAsync(
            fixture.Room.Id,
            fixture.MemberId,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            new[] { "save", "owner-changed" },
            fixture.Events);

        Assert.Equal(
            fixture.OwnerId,
            fixture.Publisher.LastPreviousOwnerId);

        Assert.Equal(
            fixture.MemberId,
            fixture.Publisher.LastNewOwnerId);

        Assert.Equal(
            fixture.MemberId,
            fixture.Room.OwnerGuestId);
    }

    [Fact]
    public async Task RepeatedOwnershipTransfer_DoesNotPublish()
    {
        var fixture = new Fixture();

        await fixture.Service.TransferOwnershipAsync(
            fixture.Room.Id,
            fixture.OwnerId,
            TestContext.Current.CancellationToken);

        Assert.Empty(fixture.Events);
        Assert.Equal(fixture.OwnerId, fixture.Room.OwnerGuestId);
    }

    [Fact]
    public async Task CloseRoom_SavesBeforePublishing()
    {
        var fixture = new Fixture();

        await fixture.Service.CloseRoomAsync(
            fixture.Room.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(RoomStatus.Closed, fixture.Room.Status);

        Assert.Equal(
            new[] { "save", "room-closed" },
            fixture.Events);
    }

    [Fact]
    public async Task CreateRoom_DoesNotPublish()
    {
        var fixture = new Fixture();

        await fixture.Service.CreateRoomAsync(
            new CreateRoomCommand("New room", "Owner"),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            new[] { "save" },
            fixture.Events);
    }

    [Fact]
    public async Task GetRoom_DoesNotPublish()
    {
        var fixture = new Fixture();

        await fixture.Service.GetRoomAsync(
            fixture.Room.Id,
            TestContext.Current.CancellationToken);

        Assert.Empty(fixture.Events);
    }

    [Fact]
    public async Task Join_SaveFailure_DoesNotPublish()
    {
        var fixture = new Fixture(joinMember: false);
        fixture.Guest.GuestId = fixture.MemberId;
        fixture.Repository.FailSave = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.JoinRoomAsync(
                new JoinRoomCommand(
                    fixture.Room.InviteCode,
                    "Member"),
                TestContext.Current.CancellationToken));

        Assert.Equal(
            new[] { "save" },
            fixture.Events);
    }

    [Fact]
    public async Task Leave_SaveFailure_DoesNotPublish()
    {
        var fixture = new Fixture();
        fixture.Guest.GuestId = fixture.MemberId;
        fixture.Repository.FailSave = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.LeaveRoomAsync(
                fixture.Room.Id,
                TestContext.Current.CancellationToken));

        Assert.Equal(
            new[] { "save" },
            fixture.Events);
    }

    [Fact]
    public async Task Close_SaveFailure_DoesNotPublish()
    {
        var fixture = new Fixture();
        fixture.Repository.FailSave = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.CloseRoomAsync(
                fixture.Room.Id,
                TestContext.Current.CancellationToken));

        Assert.Equal(
            new[] { "save" },
            fixture.Events);
    }

    [Fact]
    public async Task ConcurrencyConflict_DoesNotPublish()
    {
        var fixture = new Fixture();
        fixture.Repository.FailConcurrency = true;

        var exception =
            await Assert.ThrowsAsync<RoomApplicationException>(
                () => fixture.Service.TransferOwnershipAsync(
                    fixture.Room.Id,
                    fixture.MemberId,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            ApplicationErrorCode.ConcurrencyConflict,
            exception.ErrorCode);

        Assert.Equal(
            new[] { "save" },
            fixture.Events);
    }

    [Fact]
    public async Task UnauthorizedGuest_DoesNotPublish()
    {
        var fixture = new Fixture();

        fixture.Guest.GuestId = Guid.NewGuid();

        await Assert.ThrowsAsync<RoomApplicationException>(
            () => fixture.Service.CloseRoomAsync(
                fixture.Room.Id,
                TestContext.Current.CancellationToken));

        Assert.Empty(fixture.Events);
    }

    [Fact]
    public async Task InvalidJoin_DoesNotPublish()
    {
        var fixture = new Fixture(joinMember: false);
        fixture.Guest.GuestId = fixture.MemberId;

        await Assert.ThrowsAsync<RoomApplicationException>(
            () => fixture.Service.JoinRoomAsync(
                new JoinRoomCommand(
                    "invalid",
                    "Member"),
                TestContext.Current.CancellationToken));

        Assert.Empty(fixture.Events);
    }

    [Fact]
    public async Task PublisherFailure_DoesNotFailCommittedOperation()
    {
        var fixture = new Fixture();
        fixture.Publisher.FailPublication = true;

        await fixture.Service.RemoveMemberAsync(
            fixture.Room.Id,
            fixture.MemberId,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            new[] { "save", "member-removed" },
            fixture.Events);

        Assert.Equal(1, fixture.Repository.SaveCount);
        Assert.Null(fixture.Room.GetRole(fixture.MemberId));
    }

    [Fact]
    public async Task ClosedEventFailure_DoesNotFailCommit()
    {
        var fixture = new Fixture();
        fixture.Publisher.FailPublication = true;

        await fixture.Service.CloseRoomAsync(
            fixture.Room.Id,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            new[] { "save", "room-closed" },
            fixture.Events);

        Assert.Equal(RoomStatus.Closed, fixture.Room.Status);
        Assert.Equal(1, fixture.Repository.SaveCount);
    }

    [Fact]
    public async Task CancellationAfterSave_DoesNotSkipPublication()
    {
        var fixture = new Fixture();

        using var cancellation =
            new CancellationTokenSource();

        fixture.Repository.AfterSave = cancellation.Cancel;

        await fixture.Service.RemoveMemberAsync(
            fixture.Room.Id,
            fixture.MemberId,
            cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);

        Assert.Equal(
            new[] { "save", "member-removed" },
            fixture.Events);

        Assert.False(
            fixture.Publisher.LastCancellationToken.CanBeCanceled);
    }

    [Fact]
    public async Task OwnerCannotLeaveWhileAnotherMemberExists()
    {
        var fixture = new Fixture();

        await Assert.ThrowsAsync<RoomApplicationException>(
            () => fixture.Service.LeaveRoomAsync(
                fixture.Room.Id,
                TestContext.Current.CancellationToken));

        Assert.Empty(fixture.Events);
    }

    private sealed class Fixture
    {
        public Fixture(bool joinMember = true)
        {
            OwnerId = Guid.NewGuid();
            MemberId = Guid.NewGuid();

            Room = Room.Create(
                "Music room",
                new string('a', 22),
                OwnerId,
                "Owner",
                10,
                DateTimeOffset.UtcNow);

            if (joinMember)
            {
                Room.Join(
                    MemberId,
                    "Member",
                    Room.InviteCode,
                    DateTimeOffset.UtcNow);
            }

            Repository = new FakeRepository(this)
            {
                Room = Room
            };

            Publisher = new FakeRealtimePublisher(this);

            Guest = new FakeGuest
            {
                GuestId = OwnerId
            };

            Service = new RoomApplicationService(
                Repository,
                Guest,
                new FakeInviteCodeGenerator(),
                TimeProvider.System,
                new RoomSettings { Capacity = 10 },
                Publisher,
                NullLogger<RoomApplicationService>.Instance);
        }

        public Guid OwnerId { get; }
        public Guid MemberId { get; }
        public Room Room { get; }
        public FakeGuest Guest { get; }
        public FakeRepository Repository { get; }
        public FakeRealtimePublisher Publisher { get; }
        public RoomApplicationService Service { get; }

        public List<string> Events { get; } = [];
    }

    private sealed class FakeGuest : ICurrentGuest
    {
        public Guid? GuestId { get; set; }
    }

    private sealed class FakeInviteCodeGenerator
        : IInviteCodeGenerator
    {
        public string Generate() => new('b', 22);
    }

    private sealed class FakeRepository : IRoomRepository
    {
        private readonly Fixture _fixture;

        public FakeRepository(Fixture fixture)
        {
            _fixture = fixture;
        }

        public Room? Room { get; set; }
        public bool FailSave { get; set; }
        public bool FailConcurrency { get; set; }
        public Action? AfterSave { get; set; }
        public int SaveCount { get; private set; }

        public Task<Room?> GetByIdAsync(
            Guid roomId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                Room?.Id == roomId ? Room : null);
        }

        public Task<Room?> GetByInviteCodeAsync(
            string inviteCode,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                Room?.InviteCode == inviteCode ? Room : null);
        }

        public Task AddAsync(
            Room room,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Room = room;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            SaveCount++;
            _fixture.Events.Add("save");

            if (FailConcurrency)
            {
                throw new RoomConcurrencyException(
                    "Simulated concurrency conflict.");
            }

            if (FailSave)
            {
                throw new InvalidOperationException(
                    "Simulated persistence failure.");
            }

            AfterSave?.Invoke();

            return Task.CompletedTask;
        }
    }

    private sealed class FakeRealtimePublisher
        : IRoomRealtimePublisher
    {
        private readonly Fixture _fixture;

        public FakeRealtimePublisher(Fixture fixture)
        {
            _fixture = fixture;
        }

        public bool FailPublication { get; set; }

        public Guid? LastRoomId { get; private set; }
        public Guid? LastGuestId { get; private set; }
        public Guid? LastPreviousOwnerId { get; private set; }
        public Guid? LastNewOwnerId { get; private set; }
        public RoomQueueDetails? LastQueue { get; private set; }

        public RoomMemberRealtimeData? LastJoinedMember
        {
            get;
            private set;
        }

        public CancellationToken LastCancellationToken
        {
            get;
            private set;
        }

        public Task MemberJoinedAsync(
            Guid roomId,
            RoomMemberRealtimeData member,
            CancellationToken cancellationToken = default)
        {
            LastJoinedMember = member;

            return Record(
                "member-joined",
                roomId,
                cancellationToken);
        }

        public Task MemberLeftAsync(
            Guid roomId,
            Guid guestId,
            CancellationToken cancellationToken = default)
        {
            LastGuestId = guestId;

            return Record(
                "member-left",
                roomId,
                cancellationToken);
        }

        public Task MemberRemovedAsync(
            Guid roomId,
            Guid guestId,
            CancellationToken cancellationToken = default)
        {
            LastGuestId = guestId;

            return Record(
                "member-removed",
                roomId,
                cancellationToken);
        }

        public Task OwnerChangedAsync(
            Guid roomId,
            Guid previousOwnerGuestId,
            Guid newOwnerGuestId,
            CancellationToken cancellationToken = default)
        {
            LastPreviousOwnerId = previousOwnerGuestId;
            LastNewOwnerId = newOwnerGuestId;

            return Record(
                "owner-changed",
                roomId,
                cancellationToken);
        }

        public Task RoomClosedAsync(
            Guid roomId,
            CancellationToken cancellationToken = default)
        {
            return Record(
                "room-closed",
                roomId,
                cancellationToken);
        }

        public Task QueueChangedAsync(
            RoomQueueDetails queue,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(queue);

            LastQueue = queue;

            return Record(
                "queue-changed",
                queue.RoomId,
                cancellationToken);
        }

        private Task Record(
            string eventName,
            Guid roomId,
            CancellationToken cancellationToken)
        {
            LastRoomId = roomId;
            LastCancellationToken = cancellationToken;

            _fixture.Events.Add(eventName);

            if (FailPublication)
            {
                throw new InvalidOperationException(
                    "Simulated Realtime publication failure.");
            }

            return Task.CompletedTask;
        }
    }
}