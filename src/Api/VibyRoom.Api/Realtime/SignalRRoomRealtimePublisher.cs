using VibyRoom.Api.Realtime.Contracts;
using VibyRoom.Application.Abstractions.Realtime;
using VibyRoom.Application.Contracts.Playback;
using VibyRoom.Application.Contracts.Queue;

namespace VibyRoom.Api.Realtime;

public sealed class SignalRRoomRealtimePublisher
    : IRoomRealtimePublisher
{
    private readonly RoomPresenceCoordinator _coordinator;

    public SignalRRoomRealtimePublisher(
        RoomPresenceCoordinator coordinator)
    {
        _coordinator = coordinator
            ?? throw new ArgumentNullException(
                nameof(coordinator));
    }

    public Task MemberJoinedAsync(
        Guid roomId,
        RoomMemberRealtimeData member,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(member);

        var payload = new RoomMemberJoined(
            roomId,
            new RoomMemberEvent(
                member.GuestId,
                member.DisplayName,
                member.JoinedAtUtc));

        return _coordinator.PublishRoomEventAsync(
            roomId,
            client => client.RoomMemberJoined(payload),
            cancellationToken);
    }

    public Task MemberLeftAsync(
        Guid roomId,
        Guid guestId,
        CancellationToken cancellationToken = default)
    {
        var payload = new RoomMemberLeft(
            roomId,
            guestId);

        return _coordinator.RevokeMemberAndPublishAsync(
            roomId,
            guestId,
            client => client.RoomMemberLeft(payload),
            cancellationToken);
    }

    public Task MemberRemovedAsync(
        Guid roomId,
        Guid guestId,
        CancellationToken cancellationToken = default)
    {
        var payload = new RoomMemberRemoved(
            roomId,
            guestId);

        return _coordinator.RevokeMemberAndPublishAsync(
            roomId,
            guestId,
            client => client.RoomMemberRemoved(payload),
            cancellationToken);
    }

    public Task OwnerChangedAsync(
        Guid roomId,
        Guid previousOwnerGuestId,
        Guid newOwnerGuestId,
        CancellationToken cancellationToken = default)
    {
        var payload = new RoomOwnerChanged(
            roomId,
            previousOwnerGuestId,
            newOwnerGuestId);

        return _coordinator.PublishRoomEventAsync(
            roomId,
            client => client.RoomOwnerChanged(payload),
            cancellationToken);
    }

    public Task RoomClosedAsync(
        Guid roomId,
        CancellationToken cancellationToken = default)
    {
        var payload = new RoomClosed(roomId);

        return _coordinator.PublishRoomClosedAndRevokeAsync(
            roomId,
            client => client.RoomClosed(payload),
            cancellationToken);
    }

    public Task QueueChangedAsync(
        RoomQueueDetails queue,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queue);

        var payload = new QueueChanged(
            queue.RoomId,
            queue.Version,
            queue.Items);

        return _coordinator.PublishRoomEventAsync(
            queue.RoomId,
            client => client.QueueChanged(payload),
            cancellationToken);
    }

    public Task MusicStateChangedAsync(
        RoomMusicStateDetails musicState,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(musicState);

        return _coordinator.PublishRoomEventAsync(
            musicState.RoomId,
            client => client.MusicStateChanged(musicState),
            cancellationToken);
    }
}