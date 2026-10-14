namespace VibyRoom.Domain.Core.Entities.Rooms;

public sealed class RoomMember
{
    internal RoomMember(
        Guid guestId,
        string displayName,
        DateTimeOffset joinedAtUtc)
    {
        if (guestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Guest ID cannot be empty.",
                nameof(guestId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var normalizedDisplayName = displayName.Trim();

        if (normalizedDisplayName.Length > 40)
        {
            throw new ArgumentOutOfRangeException(
                nameof(displayName),
                "Display name cannot exceed 40 characters.");
        }

        GuestId = guestId;
        DisplayName = normalizedDisplayName;
        JoinedAtUtc = joinedAtUtc.ToUniversalTime();
    }

    public Guid GuestId { get; }

    public string DisplayName { get; }

    public DateTimeOffset JoinedAtUtc { get; }
}