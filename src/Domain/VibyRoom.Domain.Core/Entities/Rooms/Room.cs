namespace VibyRoom.Domain.Core.Entities.Rooms;

public sealed class Room
{
    private readonly List<Guid> _memberIds = [];
    private readonly IReadOnlyList<Guid> _members;

    public Room(string name, string inviteCode, Guid ownerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(inviteCode);
        ValidateUserId(ownerId, nameof(ownerId));

        Id = Guid.NewGuid();
        Name = name;
        InviteCode = inviteCode;
        OwnerId = ownerId;

        _memberIds.Add(ownerId);
        _members = _memberIds.AsReadOnly();
    }
    
    public Guid Id { get; }

    public string Name { get; private set; }

    public string InviteCode { get; }

    public Guid? OwnerId { get; private set; }

    public IReadOnlyList<Guid> Members => _members;

    public bool Join(Guid userId)
    {
        ValidateUserId(userId, nameof(userId));

        if (_memberIds.Contains(userId))
            return false;

        _memberIds.Add(userId);

        OwnerId ??= userId;

        return true;
    }

    public bool Leave(Guid userId)
    {
        ValidateUserId(userId, nameof(userId));

        var index = _memberIds.IndexOf(userId);

        if (index < 0)
            return false;

        var wasOwner = OwnerId == userId;

        _memberIds.RemoveAt(index);

        if (wasOwner)
        {
            OwnerId = _memberIds.Count == 0
                ? null
                : _memberIds[index % _memberIds.Count];
        }

        return true;
    }

    public void Rename(Guid requestedBy, string newName)
    {
        EnsureOwner(requestedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        Name = newName;
    }

    public void TransferOwnership(Guid requestedBy, Guid newOwnerId)
    {
        EnsureOwner(requestedBy);
        ValidateUserId(newOwnerId, nameof(newOwnerId));

        if (!_memberIds.Contains(newOwnerId))
        {
            throw new InvalidOperationException(
                "The new owner must be a room member.");
        }

        OwnerId = newOwnerId;
    }

    private void EnsureOwner(Guid userId)
    {
        ValidateUserId(userId, nameof(userId));

        if (OwnerId != userId)
        {
            throw new InvalidOperationException(
                "Only the room owner can perform this action.");
        }
    }

    private static void ValidateUserId(Guid userId, string parameterName)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "User ID cannot be empty.",
                parameterName);
        }
    }
}