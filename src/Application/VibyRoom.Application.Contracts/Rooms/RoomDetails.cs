using VibyRoom.Domain.Core.Entities.Rooms.Enums;

namespace VibyRoom.Application.Contracts.Rooms;

public sealed record RoomDetails
{
    public RoomDetails(
        Guid id,
        string name,
        RoomStatus status,
        string inviteCode,
        int capacity,
        IReadOnlyList<RoomMemberDetails> members)
    {
        ArgumentNullException.ThrowIfNull(members);

        Id = id;
        Name = name;
        Status = status;
        InviteCode = inviteCode;
        Capacity = capacity;
        Members = Array.AsReadOnly(members.ToArray());
    }

    public Guid Id { get; }

    public string Name { get; }

    public RoomStatus Status { get; }

    public string InviteCode { get; }

    public int Capacity { get; }

    public IReadOnlyList<RoomMemberDetails> Members { get; }
}