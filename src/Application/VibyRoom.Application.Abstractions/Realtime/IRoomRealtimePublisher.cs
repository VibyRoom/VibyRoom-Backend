using VibyRoom.Application.Contracts.Playback;
using VibyRoom.Application.Contracts.Queue;

namespace VibyRoom.Application.Abstractions.Realtime;

public interface IRoomRealtimePublisher
{
    Task MemberJoinedAsync(
        Guid roomId,
        RoomMemberRealtimeData member,
        CancellationToken cancellationToken = default);

    Task MemberLeftAsync(
        Guid roomId,
        Guid guestId,
        CancellationToken cancellationToken = default);

    Task MemberRemovedAsync(
        Guid roomId,
        Guid guestId,
        CancellationToken cancellationToken = default);

    Task OwnerChangedAsync(
        Guid roomId,
        Guid previousOwnerGuestId,
        Guid newOwnerGuestId,
        CancellationToken cancellationToken = default);

    Task RoomClosedAsync(
        Guid roomId,
        CancellationToken cancellationToken = default);

    Task QueueChangedAsync(
        RoomQueueDetails queue,
        CancellationToken cancellationToken = default);

    Task MusicStateChangedAsync(
        RoomMusicStateDetails musicState,
        CancellationToken cancellationToken = default);
}