namespace VibyRoom.Application.Abstractions.Rooms;

public interface IRoomMembershipQuery
{
    Task<bool> IsActiveMemberAsync(
        Guid roomId,
        Guid guestId,
        CancellationToken cancellationToken = default);
}