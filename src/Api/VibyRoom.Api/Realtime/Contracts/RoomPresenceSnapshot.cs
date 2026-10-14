namespace VibyRoom.Api.Realtime.Contracts;

public sealed record RoomPresenceSnapshot
{
    public RoomPresenceSnapshot(
        Guid roomId,
        IReadOnlyList<Guid> onlineGuestIds)
    {
        if (roomId == Guid.Empty)
        {
            throw new ArgumentException(
                "Room ID cannot be empty.",
                nameof(roomId));
        }

        ArgumentNullException.ThrowIfNull(onlineGuestIds);

        if (onlineGuestIds.Any(guestId => guestId == Guid.Empty))
        {
            throw new ArgumentException(
                "Online guest IDs cannot contain an empty GUID.",
                nameof(onlineGuestIds));
        }

        RoomId = roomId;

        OnlineGuestIds = Array.AsReadOnly(
            onlineGuestIds
                .Distinct()
                .Order()
                .ToArray());
    }

    public Guid RoomId { get; }

    public IReadOnlyList<Guid> OnlineGuestIds { get; }
}