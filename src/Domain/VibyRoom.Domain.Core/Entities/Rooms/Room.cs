using VibyRoom.Domain.Core.Entities.Rooms.Enums;
using VibyRoom.Domain.Core.Entities.Rooms.Exceprions;

namespace VibyRoom.Domain.Core.Entities.Rooms;

public sealed class Room
{
    private const int MaxNameLength = 100;
    private const int InviteCodeLength = 22;

    private readonly List<RoomMember> _members = [];
    private readonly IReadOnlyCollection<RoomMember> _membersView;

    private Room(
        string name,
        string inviteCode,
        int capacity,
        RoomMember owner)
        : this(
            Guid.NewGuid(),
            name,
            inviteCode,
            RoomStatus.Active,
            owner.GuestId,
            capacity)
    {
        _members.Add(owner);
    }

    private Room(
        Guid id,
        string name,
        string inviteCode,
        RoomStatus status,
        Guid? ownerGuestId,
        int capacity)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Room ID cannot be empty.",
                nameof(id));
        }

        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                "Room capacity must be greater than zero.");
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                "Unknown room status.");
        }

        if (status == RoomStatus.Active &&
            (!ownerGuestId.HasValue ||
             ownerGuestId.Value == Guid.Empty))
        {
            throw new ArgumentException(
                "Active room must have a valid owner.",
                nameof(ownerGuestId));
        }

        if (status == RoomStatus.Closed &&
            ownerGuestId.HasValue)
        {
            throw new ArgumentException(
                "Closed room cannot have an owner.",
                nameof(ownerGuestId));
        }

        Id = id;
        Name = ValidateName(name);
        InviteCode = ValidateInviteCode(inviteCode);
        Status = status;
        OwnerGuestId = ownerGuestId;
        Capacity = capacity;

        _membersView = _members.AsReadOnly();
    }

    public Guid Id { get; }

    public string Name { get; private set; }

    public string InviteCode { get; }

    public RoomStatus Status { get; private set; }

    public Guid? OwnerGuestId { get; private set; }

    public int Capacity { get; }

    public IReadOnlyCollection<RoomMember> Members => _membersView;

    public static Room Create(
        string name,
        string inviteCode,
        Guid ownerGuestId,
        string ownerDisplayName,
        int capacity,
        DateTimeOffset joinedAtUtc)
    {
        var normalizedName = ValidateName(name);
        var validatedInviteCode = ValidateInviteCode(inviteCode);

        ValidateGuestId(ownerGuestId, nameof(ownerGuestId));

        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                "Room capacity must be greater than zero.");
        }

        var owner = new RoomMember(
            ownerGuestId,
            ownerDisplayName,
            joinedAtUtc);

        return new Room(
            normalizedName,
            validatedInviteCode,
            capacity,
            owner);
    }

    public bool Join(
        Guid guestId,
        string displayName,
        string inviteCode,
        DateTimeOffset joinedAtUtc)
    {
        EnsureActive();

        ValidateGuestId(guestId, nameof(guestId));

        if (!string.Equals(
                InviteCode,
                inviteCode,
                StringComparison.Ordinal))
        {
            throw new RoomDomainException(
                RoomDomainErrorCode.InvalidInviteCode,
                "Invalid room invitation code.");
        }

        var newMember = new RoomMember(
            guestId,
            displayName,
            joinedAtUtc);

        if (_members.Any(member => member.GuestId == guestId))
        {
            return false;
        }

        if (_members.Count >= Capacity)
        {
            throw new RoomDomainException(
                RoomDomainErrorCode.RoomFull,
                "Room capacity has been reached.");
        }

        _members.Add(newMember);

        return true;
    }

    public void Leave(Guid guestId)
    {
        EnsureActive();

        ValidateGuestId(guestId, nameof(guestId));

        var member = _members.FirstOrDefault(
            member => member.GuestId == guestId);

        if (member is null)
        {
            throw new RoomDomainException(
                RoomDomainErrorCode.MemberNotFound,
                "Guest is not a member of this room.");
        }

        if (OwnerGuestId == guestId)
        {
            if (_members.Count > 1)
            {
                throw new RoomDomainException(
                    RoomDomainErrorCode.OwnershipTransferRequired,
                    "Owner must transfer ownership or close the room before leaving.");
            }

            Close(guestId);
            return;
        }

        _members.Remove(member);
    }

    public void RemoveMember(
        Guid requestedByGuestId,
        Guid targetGuestId)
    {
        EnsureOwner(requestedByGuestId);

        ValidateGuestId(
            targetGuestId,
            nameof(targetGuestId));

        if (targetGuestId == OwnerGuestId)
        {
            throw new RoomDomainException(
                RoomDomainErrorCode.InvalidOperation,
                "Room owner cannot be removed.");
        }

        var member = _members.FirstOrDefault(
            member => member.GuestId == targetGuestId);

        if (member is null)
        {
            throw new RoomDomainException(
                RoomDomainErrorCode.MemberNotFound,
                "Target guest is not a member of this room.");
        }

        _members.Remove(member);
    }

    public void TransferOwnership(
        Guid requestedByGuestId,
        Guid newOwnerGuestId)
    {
        EnsureOwner(requestedByGuestId);

        ValidateGuestId(
            newOwnerGuestId,
            nameof(newOwnerGuestId));

        if (_members.All(
                member => member.GuestId != newOwnerGuestId))
        {
            throw new RoomDomainException(
                RoomDomainErrorCode.MemberNotFound,
                "New owner must be a current room member.");
        }

        if (OwnerGuestId == newOwnerGuestId)
        {
            return;
        }

        OwnerGuestId = newOwnerGuestId;
    }

    public void Close(Guid requestedByGuestId)
    {
        EnsureOwner(requestedByGuestId);

        _members.Clear();
        OwnerGuestId = null;
        Status = RoomStatus.Closed;
    }

    public RoomRole? GetRole(Guid guestId)
    {
        ValidateGuestId(guestId, nameof(guestId));

        if (Status == RoomStatus.Closed)
        {
            return null;
        }

        if (_members.All(
                member => member.GuestId != guestId))
        {
            return null;
        }

        return OwnerGuestId == guestId
            ? RoomRole.Owner
            : RoomRole.Member;
    }

    public void Rename(
        Guid requestedBy,
        string newName)
    {
        EnsureOwner(requestedBy);

        Name = ValidateName(newName);
    }

    private void EnsureActive()
    {
        if (Status != RoomStatus.Active)
        {
            throw new RoomDomainException(
                RoomDomainErrorCode.RoomClosed,
                "Room is closed.");
        }
    }

    private void EnsureOwner(Guid requestedByGuestId)
    {
        EnsureActive();

        ValidateGuestId(
            requestedByGuestId,
            nameof(requestedByGuestId));

        if (OwnerGuestId != requestedByGuestId)
        {
            throw new RoomDomainException(
                RoomDomainErrorCode.InsufficientPermissions,
                "Only the room owner can perform this action.");
        }
    }

    private static string ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var normalizedName = name.Trim();

        if (normalizedName.Length > MaxNameLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(name),
                $"Room name cannot exceed {MaxNameLength} characters.");
        }

        return normalizedName;
    }

    private static string ValidateInviteCode(string inviteCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inviteCode);

        if (inviteCode.Length != InviteCodeLength ||
            inviteCode.Any(character =>
                !IsBase64UrlCharacter(character)))
        {
            throw new ArgumentException(
                $"Invite code must contain exactly {InviteCodeLength} base64url characters.",
                nameof(inviteCode));
        }

        return inviteCode;
    }

    private static bool IsBase64UrlCharacter(char character)
    {
        return character is
            >= 'A' and <= 'Z' or
            >= 'a' and <= 'z' or
            >= '0' and <= '9' or
            '-' or '_';
    }

    private static void ValidateGuestId(
        Guid guestId,
        string parameterName)
    {
        if (guestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Guest ID cannot be empty.",
                parameterName);
        }
    }
}