namespace VibyRoom.Api.Realtime.Contracts;

public sealed record RoomSubscriptionRevoked
{
    public const string MembershipEnded = "membershipEnded";
    public const string RoomClosed = "roomClosed";

    public RoomSubscriptionRevoked(
        Guid roomId,
        string reason)
    {
        if (roomId == Guid.Empty)
        {
            throw new ArgumentException(
                "Room ID cannot be empty.",
                nameof(roomId));
        }

        if (reason is not (MembershipEnded or RoomClosed))
        {
            throw new ArgumentOutOfRangeException(
                nameof(reason),
                reason,
                "Unknown subscription revocation reason.");
        }

        RoomId = roomId;
        Reason = reason;
    }

    public Guid RoomId { get; }

    public string Reason { get; }
}