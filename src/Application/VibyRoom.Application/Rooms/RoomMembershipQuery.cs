using VibyRoom.Application.Abstractions.Repositories;
using VibyRoom.Application.Abstractions.Rooms;
using VibyRoom.Domain.Core.Entities.Rooms.Enums;

namespace VibyRoom.Application.Rooms;

public sealed class RoomMembershipQuery : IRoomMembershipQuery
{
    private readonly IRoomRepository _roomRepository;

    public RoomMembershipQuery(IRoomRepository roomRepository)
    {
        _roomRepository = roomRepository
                          ?? throw new ArgumentNullException(nameof(roomRepository));
    }

    public async Task<bool> IsActiveMemberAsync(
        Guid roomId,
        Guid guestId,
        CancellationToken cancellationToken = default)
    {
        if (roomId == Guid.Empty || guestId == Guid.Empty)
        {
            return false;
        }

        var room = await _roomRepository.GetByIdAsync(
            roomId,
            cancellationToken);

        if (room is null || room.Status != RoomStatus.Active)
        {
            return false;
        }

        return room.GetRole(guestId) is not null;
    }
}