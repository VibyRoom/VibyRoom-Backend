namespace VibyRoom.Application.Abstractions.Realtime;

public interface IRoomPresenceNotifier
{
    Task RevokeMemberAsync(
        Guid roomId,
        Guid guestId,
        CancellationToken cancellationToken = default);

    Task RevokeRoomAsync(
        Guid roomId,
        CancellationToken cancellationToken = default);
}