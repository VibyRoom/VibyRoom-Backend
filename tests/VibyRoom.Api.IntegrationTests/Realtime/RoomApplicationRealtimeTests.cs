using Microsoft.Extensions.Logging.Abstractions;
using VibyRoom.Application.Abstractions.Authentication;
using VibyRoom.Application.Abstractions.Invitations;
using VibyRoom.Application.Abstractions.Repositories;
using VibyRoom.Application.Abstractions.Realtime;
using VibyRoom.Application.Configuration;
using VibyRoom.Application.Contracts.Rooms;
using VibyRoom.Application.Rooms;
using VibyRoom.Domain.Core.Entities.Rooms;
using VibyRoom.Domain.Core.Entities.Rooms.Enums;
using Xunit;

namespace VibyRoom.Api.IntegrationTests.Realtime;

public sealed class RoomApplicationRealtimeTests
{
    [Fact]
    public async Task LeaveMember_SavesBeforeRevocation()
    {
        var fixture = new Fixture();
        fixture.Guest.GuestId = fixture.MemberId;

        await fixture.Service.LeaveRoomAsync(fixture.Room.Id, TestContext.Current.CancellationToken);

        Assert.Equal(
            new[] { "save", "revoke-member" },
            fixture.Events);

        Assert.Equal(
            fixture.MemberId,
            fixture.Notifier.LastRevokedGuestId);
    }

    [Fact]
    public async Task LeaveSoleOwner_ClosesRoomAndRevokesRoom()
    {
        var fixture = new Fixture(joinMember: false);

        await fixture.Service.LeaveRoomAsync(fixture.Room.Id, TestContext.Current.CancellationToken);

        Assert.Equal(RoomStatus.Closed, fixture.Room.Status);

        Assert.Equal(
            new[] { "save", "revoke-room" },
            fixture.Events);
    }

    [Fact]
    public async Task RemoveMember_SavesBeforeRevocation()
    {
        var fixture = new Fixture();

        await fixture.Service.RemoveMemberAsync(fixture.Room.Id, fixture.MemberId, TestContext.Current.CancellationToken);

        Assert.Equal(
            new[] { "save", "revoke-member" },
            fixture.Events);

        Assert.Equal(
            fixture.MemberId,
            fixture.Notifier.LastRevokedGuestId);
    }

    [Fact]
    public async Task CloseRoom_SavesBeforeRevocation()
    {
        var fixture = new Fixture();

        await fixture.Service.CloseRoomAsync(fixture.Room.Id, TestContext.Current.CancellationToken);

        Assert.Equal(
            new[] { "save", "revoke-room" },
            fixture.Events);

        Assert.Equal(RoomStatus.Closed, fixture.Room.Status);
    }

    [Fact]
    public async Task TransferOwnership_DoesNotChangePresence()
    {
        var fixture = new Fixture();

        await fixture.Service.TransferOwnershipAsync(fixture.Room.Id, fixture.MemberId, TestContext.Current.CancellationToken);

        Assert.Equal(
            new[] { "save" },
            fixture.Events);

        Assert.Equal(
            fixture.MemberId,
            fixture.Room.OwnerGuestId);
    }

    [Fact]
    public async Task CreateRoom_DoesNotNotifyPresence()
    {
        var fixture = new Fixture();

        await fixture.Service.CreateRoomAsync(new CreateRoomCommand("New room", "Owner"), TestContext.Current.CancellationToken);

        Assert.Equal(
            new[] { "save" },
            fixture.Events);
    }

    [Fact]
    public async Task JoinRoom_DoesNotNotifyPresence()
    {
        var fixture = new Fixture(joinMember: false);
        fixture.Guest.GuestId = fixture.MemberId;

        var result = await fixture.Service.JoinRoomAsync(new JoinRoomCommand(
                fixture.Room.InviteCode,
                "Member"), TestContext.Current.CancellationToken);

        Assert.True(result.Joined);

        Assert.Equal(
            new[] { "save" },
            fixture.Events);
    }

    [Fact]
    public async Task SaveFails_DoesNotRevokeMember()
    {
        var fixture = new Fixture();
        fixture.Guest.GuestId = fixture.MemberId;
        fixture.Repository.FailSave = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.LeaveRoomAsync(fixture.Room.Id, TestContext.Current.CancellationToken));

        Assert.Equal(
            new[] { "save" },
            fixture.Events);
    }

    [Fact]
    public async Task SaveFails_DoesNotRevokeRoom()
    {
        var fixture = new Fixture();
        fixture.Repository.FailSave = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Service.CloseRoomAsync(fixture.Room.Id, TestContext.Current.CancellationToken));

        Assert.Equal(
            new[] { "save" },
            fixture.Events);
    }

    [Fact]
    public async Task NotifierFailure_DoesNotFailCommittedOperation()
    {
        var fixture = new Fixture();

        fixture.Notifier.FailRevocation = true;

        await fixture.Service.RemoveMemberAsync(fixture.Room.Id, fixture.MemberId, TestContext.Current.CancellationToken);

        Assert.Equal(
            new[] { "save", "revoke-member" },
            fixture.Events);

        Assert.Null(
            fixture.Room.GetRole(fixture.MemberId));
    }

    [Fact]
    public async Task CancellationAfterSave_DoesNotSkipRevocation()
    {
        var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();

        fixture.Repository.AfterSave = cancellation.Cancel;

        await fixture.Service.RemoveMemberAsync(
            fixture.Room.Id,
            fixture.MemberId,
            cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);

        Assert.Equal(
            new[] { "save", "revoke-member" },
            fixture.Events);

        Assert.False(
            fixture.Notifier.LastCancellationToken
                .CanBeCanceled);
    }

    [Fact]
    public async Task OwnerCannotLeaveWhileAnotherMemberExists()
    {
        var fixture = new Fixture();

        await Assert.ThrowsAnyAsync<Exception>(
            () => fixture.Service.LeaveRoomAsync(fixture.Room.Id, TestContext.Current.CancellationToken));

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

            Notifier = new FakeNotifier(this);

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
                Notifier,
                NullLogger<RoomApplicationService>.Instance);
        }

        public Guid OwnerId { get; }

        public Guid MemberId { get; }

        public Room Room { get; }

        public FakeGuest Guest { get; }

        public FakeRepository Repository { get; }

        public FakeNotifier Notifier { get; }

        public RoomApplicationService Service { get; }

        public List<string> Events { get; } = [];
    }

    private sealed class FakeGuest : ICurrentGuest
    {
        public Guid? GuestId { get; set; }
    }

    private sealed class FakeInviteCodeGenerator : IInviteCodeGenerator
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

        public Action? AfterSave { get; set; }

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

            _fixture.Events.Add("save");

            if (FailSave)
            {
                throw new InvalidOperationException(
                    "Simulated persistence failure.");
            }

            AfterSave?.Invoke();

            return Task.CompletedTask;
        }
    }

    private sealed class FakeNotifier : IRoomPresenceNotifier
    {
        private readonly Fixture _fixture;

        public FakeNotifier(Fixture fixture)
        {
            _fixture = fixture;
        }

        public Guid? LastRevokedGuestId { get; private set; }

        public CancellationToken LastCancellationToken
        {
            get;
            private set;
        }

        public bool FailRevocation { get; set; }

        public Task RevokeMemberAsync(
            Guid roomId,
            Guid guestId,
            CancellationToken cancellationToken = default)
        {
            _fixture.Events.Add("revoke-member");

            LastRevokedGuestId = guestId;
            LastCancellationToken = cancellationToken;

            return CompleteRevocation();
        }

        public Task RevokeRoomAsync(
            Guid roomId,
            CancellationToken cancellationToken = default)
        {
            _fixture.Events.Add("revoke-room");

            LastCancellationToken = cancellationToken;

            return CompleteRevocation();
        }

        private Task CompleteRevocation()
        {
            if (FailRevocation)
            {
                throw new InvalidOperationException(
                    "Simulated Realtime failure.");
            }

            return Task.CompletedTask;
        }
    }
}