using Microsoft.Extensions.Logging;
using VibyRoom.Application.Abstractions.Authentication;
using VibyRoom.Application.Abstractions.Exceptions;
using VibyRoom.Application.Abstractions.Invitations;
using VibyRoom.Application.Abstractions.Repositories;
using VibyRoom.Application.Abstractions.Realtime;
using VibyRoom.Application.Configuration;
using VibyRoom.Application.Contracts.Rooms;
using VibyRoom.Application.Errors;
using VibyRoom.Domain.Core.Entities.Rooms;
using VibyRoom.Domain.Core.Entities.Rooms.Enums;
using VibyRoom.Domain.Core.Entities.Rooms.Exceprions;

namespace VibyRoom.Application.Rooms;

public sealed class RoomApplicationService
{
    private const int MaxRoomNameLength = 100;
    private const int MaxDisplayNameLength = 40;
    private const int InviteCodeLength = 22;

    private readonly IRoomRepository _roomRepository;
    private readonly ICurrentGuest _currentGuest;
    private readonly IInviteCodeGenerator _inviteCodeGenerator;
    private readonly TimeProvider _timeProvider;
    private readonly RoomSettings _roomSettings;
    private readonly IRoomRealtimePublisher _roomRealtimePublisher;
    private readonly ILogger<RoomApplicationService> _logger;

    public RoomApplicationService(
        IRoomRepository roomRepository,
        ICurrentGuest currentGuest,
        IInviteCodeGenerator inviteCodeGenerator,
        TimeProvider timeProvider,
        RoomSettings roomSettings,
        IRoomRealtimePublisher roomRealtimePublisher,
        ILogger<RoomApplicationService> logger)
    {
        _roomRepository = roomRepository
            ?? throw new ArgumentNullException(nameof(roomRepository));

        _currentGuest = currentGuest
            ?? throw new ArgumentNullException(nameof(currentGuest));

        _inviteCodeGenerator = inviteCodeGenerator
            ?? throw new ArgumentNullException(nameof(inviteCodeGenerator));

        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));

        _roomSettings = roomSettings
            ?? throw new ArgumentNullException(nameof(roomSettings));

        _roomRealtimePublisher = roomRealtimePublisher
            ?? throw new ArgumentNullException(nameof(roomRealtimePublisher));

        _logger = logger
            ?? throw new ArgumentNullException(nameof(logger));

        if (_roomSettings.Capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(roomSettings),
                "Room capacity must be greater than zero.");
        }
    }

    public async Task<RoomDetails> CreateRoomAsync(
        CreateRoomCommand command,
        CancellationToken cancellationToken = default)
    {
        var guestId = RequireCurrentGuest();

        if (command is null)
        {
            throw ValidationError("Create room command is required.");
        }

        var name = ValidateText(
            command.Name,
            MaxRoomNameLength,
            nameof(command.Name));

        var displayName = ValidateText(
            command.DisplayName,
            MaxDisplayNameLength,
            nameof(command.DisplayName));

        var inviteCode = _inviteCodeGenerator.Generate();

        var room = Room.Create(
            name,
            inviteCode,
            guestId,
            displayName,
            _roomSettings.Capacity,
            _timeProvider.GetUtcNow());

        await _roomRepository.AddAsync(
            room,
            cancellationToken);

        await SaveChangesAsync(cancellationToken);

        return MapRoomDetails(room);
    }

    public async Task<RoomDetails> GetRoomAsync(
        Guid roomId,
        CancellationToken cancellationToken = default)
    {
        var guestId = RequireCurrentGuest();

        var room = await GetActiveMemberRoomAsync(
            roomId,
            guestId,
            cancellationToken);

        return MapRoomDetails(room);
    }

    public async Task<RoomPreview> GetInvitationPreviewAsync(
        string inviteCode,
        CancellationToken cancellationToken = default)
    {
        ValidateInviteCode(inviteCode);

        var room = await _roomRepository.GetByInviteCodeAsync(
            inviteCode,
            cancellationToken);

        if (room is null || room.Status != RoomStatus.Active)
        {
            throw new RoomApplicationException(
                ApplicationErrorCode.InvalidInvitation,
                "Invitation is invalid or no longer available.");
        }

        return new RoomPreview(
            room.Id,
            room.Name);
    }

    public async Task<JoinRoomResult> JoinRoomAsync(
        JoinRoomCommand command,
        CancellationToken cancellationToken = default)
    {
        var guestId = RequireCurrentGuest();

        if (command is null)
        {
            throw ValidationError("Join room command is required.");
        }

        ValidateInviteCode(command.InviteCode);

        var displayName = ValidateText(
            command.DisplayName,
            MaxDisplayNameLength,
            nameof(command.DisplayName));

        var room = await _roomRepository.GetByInviteCodeAsync(
            command.InviteCode,
            cancellationToken);

        if (room is null || room.Status != RoomStatus.Active)
        {
            throw new RoomApplicationException(
                ApplicationErrorCode.InvalidInvitation,
                "Invitation is invalid or no longer available.");
        }

        bool joined;

        try
        {
            joined = room.Join(
                guestId,
                displayName,
                command.InviteCode,
                _timeProvider.GetUtcNow());
        }
        catch (RoomDomainException exception)
        {
            throw MapDomainException(
                exception,
                roomClosedCode: ApplicationErrorCode.InvalidInvitation);
        }

        if (joined)
        {
            var member = room.Members.Single(
                item => item.GuestId == guestId);

            var memberData = new RoomMemberRealtimeData(
                member.GuestId,
                member.DisplayName,
                member.JoinedAtUtc);

            await SaveChangesAsync(cancellationToken);

            await TryPublishEventAsync(
                room.Id,
                "RoomMemberJoined",
                () => _roomRealtimePublisher.MemberJoinedAsync(
                    room.Id,
                    memberData,
                    CancellationToken.None));
        }

        return new JoinRoomResult(
            MapRoomDetails(room),
            joined);
    }

    public async Task LeaveRoomAsync(
        Guid roomId,
        CancellationToken cancellationToken = default)
    {
        var guestId = RequireCurrentGuest();

        var room = await GetActiveMemberRoomAsync(
            roomId,
            guestId,
            cancellationToken);

        try
        {
            room.Leave(guestId);
        }
        catch (RoomDomainException exception)
        {
            throw MapDomainException(
                exception,
                memberNotFoundCode: ApplicationErrorCode.NotMember);
        }

        await SaveChangesAsync(cancellationToken);

        if (room.Status == RoomStatus.Closed)
        {
            await TryPublishEventAsync(
                roomId,
                "RoomClosed",
                () => _roomRealtimePublisher.RoomClosedAsync(
                    roomId,
                    CancellationToken.None));
        }
        else
        {
            await TryPublishEventAsync(
                roomId,
                "RoomMemberLeft",
                () => _roomRealtimePublisher.MemberLeftAsync(
                    roomId,
                    guestId,
                    CancellationToken.None));
        }
    }

    public async Task RemoveMemberAsync(
        Guid roomId,
        Guid targetGuestId,
        CancellationToken cancellationToken = default)
    {
        var guestId = RequireCurrentGuest();

        var room = await GetActiveMemberRoomAsync(
            roomId,
            guestId,
            cancellationToken);

        EnsureOwner(room, guestId);

        ValidateGuestId(
            targetGuestId,
            nameof(targetGuestId));

        try
        {
            room.RemoveMember(
                guestId,
                targetGuestId);
        }
        catch (RoomDomainException exception)
        {
            throw MapDomainException(
                exception,
                invalidOperationCode: ApplicationErrorCode.Validation);
        }

        await SaveChangesAsync(cancellationToken);

        await TryPublishEventAsync(
            roomId,
            "RoomMemberRemoved",
            () => _roomRealtimePublisher.MemberRemovedAsync(
                roomId,
                targetGuestId,
                CancellationToken.None));
    }

    public async Task TransferOwnershipAsync(
        Guid roomId,
        Guid newOwnerGuestId,
        CancellationToken cancellationToken = default)
    {
        var guestId = RequireCurrentGuest();

        var room = await GetActiveMemberRoomAsync(
            roomId,
            guestId,
            cancellationToken);

        EnsureOwner(room, guestId);

        ValidateGuestId(
            newOwnerGuestId,
            nameof(newOwnerGuestId));

        var previousOwnerGuestId = room.OwnerGuestId
            ?? throw new InvalidOperationException(
                "An active room must have an owner.");

        try
        {
            room.TransferOwnership(
                guestId,
                newOwnerGuestId);
        }
        catch (RoomDomainException exception)
        {
            throw MapDomainException(exception);
        }

        if (room.OwnerGuestId != previousOwnerGuestId)
        {
            var committedNewOwnerGuestId = room.OwnerGuestId
                ?? throw new InvalidOperationException(
                    "An active room must have an owner.");

            await SaveChangesAsync(cancellationToken);

            await TryPublishEventAsync(
                roomId,
                "RoomOwnerChanged",
                () => _roomRealtimePublisher.OwnerChangedAsync(
                    roomId,
                    previousOwnerGuestId,
                    committedNewOwnerGuestId,
                    CancellationToken.None));
        }
    }

    public async Task CloseRoomAsync(
        Guid roomId,
        CancellationToken cancellationToken = default)
    {
        var guestId = RequireCurrentGuest();

        var room = await GetActiveMemberRoomAsync(
            roomId,
            guestId,
            cancellationToken);

        EnsureOwner(room, guestId);

        try
        {
            room.Close(guestId);
        }
        catch (RoomDomainException exception)
        {
            throw MapDomainException(exception);
        }

        await SaveChangesAsync(cancellationToken);

        await TryPublishEventAsync(
            roomId,
            "RoomClosed",
            () => _roomRealtimePublisher.RoomClosedAsync(
                roomId,
                CancellationToken.None));
    }

    private async Task TryPublishEventAsync(
        Guid roomId,
        string eventName,
        Func<Task> publish)
    {
        try
        {
            // Persistence has already been committed. The realtime
            // operation must not use an aborted HTTP request token.
            await publish();
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Room state change was committed, but Realtime " +
                "operation {EventName} failed for room {RoomId}.",
                eventName,
                roomId);
        }
    }

    private Guid RequireCurrentGuest()
    {
        var guestId = _currentGuest.GuestId;

        if (!guestId.HasValue || guestId.Value == Guid.Empty)
        {
            throw new RoomApplicationException(
                ApplicationErrorCode.GuestSessionRequired,
                "An active guest session is required.");
        }

        return guestId.Value;
    }

    private async Task<Room> GetActiveMemberRoomAsync(
        Guid roomId,
        Guid guestId,
        CancellationToken cancellationToken)
    {
        ValidateRoomId(roomId);

        var room = await _roomRepository.GetByIdAsync(
            roomId,
            cancellationToken);

        if (room is null ||
            room.Status != RoomStatus.Active ||
            room.GetRole(guestId) is null)
        {
            throw new RoomApplicationException(
                ApplicationErrorCode.RoomNotFound,
                "Room was not found.");
        }

        return room;
    }

    private static void EnsureOwner(
        Room room,
        Guid guestId)
    {
        if (room.GetRole(guestId) != RoomRole.Owner)
        {
            throw new RoomApplicationException(
                ApplicationErrorCode.AccessDenied,
                "Only the room owner can perform this action.");
        }
    }

    private static void ValidateRoomId(
        Guid roomId)
    {
        if (roomId == Guid.Empty)
        {
            throw ValidationError(
                "Room ID cannot be empty.");
        }
    }

    private static void ValidateGuestId(
        Guid guestId,
        string parameterName)
    {
        if (guestId == Guid.Empty)
        {
            throw ValidationError(
                $"{parameterName} cannot be empty.");
        }
    }

    private static string ValidateText(
        string? value,
        int maxLength,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw ValidationError(
                $"{parameterName} is required.");
        }

        var normalized = value.Trim();

        if (normalized.Length > maxLength)
        {
            throw ValidationError(
                $"{parameterName} cannot exceed {maxLength} characters.");
        }

        return normalized;
    }

    private static void ValidateInviteCode(
        string? inviteCode)
    {
        if (inviteCode is null ||
            inviteCode.Length != InviteCodeLength ||
            inviteCode.Any(
                character => !IsBase64UrlCharacter(character)))
        {
            throw new RoomApplicationException(
                ApplicationErrorCode.InvalidInvitation,
                "Invitation code has an invalid format.");
        }
    }

    private static bool IsBase64UrlCharacter(
        char character)
    {
        return character is
            >= 'A' and <= 'Z' or
            >= 'a' and <= 'z' or
            >= '0' and <= '9' or
            '-' or '_';
    }

    private static RoomDetails MapRoomDetails(
        Room room)
    {
        var members = room.Members
            .Select(member =>
            {
                var role = room.GetRole(member.GuestId)
                    ?? throw new InvalidOperationException(
                        "An active room member must have a valid role.");

                return new RoomMemberDetails(
                    member.GuestId,
                    member.DisplayName,
                    role,
                    member.JoinedAtUtc);
            })
            .ToArray();

        return new RoomDetails(
            room.Id,
            room.Name,
            room.Status,
            room.InviteCode,
            room.Capacity,
            members);
    }

    private async Task SaveChangesAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await _roomRepository.SaveChangesAsync(
                cancellationToken);
        }
        catch (RoomConcurrencyException exception)
        {
            throw new RoomApplicationException(
                ApplicationErrorCode.ConcurrencyConflict,
                "The room was modified concurrently. Reload it and retry.",
                exception);
        }
        catch (RoomInviteCodeConflictException exception)
        {
            throw new RoomApplicationException(
                ApplicationErrorCode.InviteCodeConflict,
                "Failed to create room because the invitation " +
                "code already exists.",
                exception);
        }
    }

    private static RoomApplicationException ValidationError(
        string message)
    {
        return new RoomApplicationException(
            ApplicationErrorCode.Validation,
            message);
    }

    private static RoomApplicationException MapDomainException(
        RoomDomainException exception,
        ApplicationErrorCode? invalidOperationCode = null,
        ApplicationErrorCode memberNotFoundCode =
            ApplicationErrorCode.MemberNotFound,
        ApplicationErrorCode roomClosedCode =
            ApplicationErrorCode.RoomClosed)
    {
        var errorCode = exception.ErrorCode switch
        {
            RoomDomainErrorCode.RoomClosed =>
                roomClosedCode,

            RoomDomainErrorCode.RoomFull =>
                ApplicationErrorCode.RoomFull,

            RoomDomainErrorCode.InvalidInviteCode =>
                ApplicationErrorCode.InvalidInvitation,

            RoomDomainErrorCode.MemberNotFound =>
                memberNotFoundCode,

            RoomDomainErrorCode.InsufficientPermissions =>
                ApplicationErrorCode.AccessDenied,

            RoomDomainErrorCode.OwnershipTransferRequired =>
                ApplicationErrorCode.OwnerTransferRequired,

            RoomDomainErrorCode.InvalidOperation
                when invalidOperationCode.HasValue =>
                invalidOperationCode.Value,

            _ => throw new InvalidOperationException(
                "An unmapped room domain error was encountered.",
                exception)
        };

        return new RoomApplicationException(
            errorCode,
            exception.Message,
            exception);
    }
}