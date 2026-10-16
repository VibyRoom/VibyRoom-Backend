using Microsoft.Extensions.Logging;
using VibyRoom.Application.Abstractions.Authentication;
using VibyRoom.Application.Abstractions.Exceptions;
using VibyRoom.Application.Abstractions.Repositories;
using VibyRoom.Application.Abstractions.Realtime;
using VibyRoom.Application.Configuration;
using VibyRoom.Application.Contracts.Queue;
using VibyRoom.Application.Errors;
using VibyRoom.Application.Music;
using VibyRoom.Domain.Core.Entities.Rooms;
using VibyRoom.Domain.Core.Entities.Rooms.Enums;
using VibyRoom.Domain.Music.Queue;
using VibyRoom.Domain.Music.Queue.Exceptions;
using VibyRoom.Domain.Music.Tracks;

namespace VibyRoom.Application.Queue;

public sealed class RoomQueueApplicationService
{
    private static readonly TimeSpan PublishTimeout =
        TimeSpan.FromSeconds(5);

    private readonly IRoomRepository _rooms;
    private readonly IRoomMusicSessionRepository _queues;
    private readonly IRoomQueueWriteExecutor _writer;
    private readonly ICurrentGuest _currentGuest;
    private readonly MusicCatalogService _catalog;
    private readonly TimeProvider _timeProvider;
    private readonly RoomQueueSettings _settings;
    private readonly IRoomRealtimePublisher _realtime;
    private readonly ILogger<RoomQueueApplicationService> _logger;

    public RoomQueueApplicationService(
        IRoomRepository rooms,
        IRoomMusicSessionRepository queues,
        IRoomQueueWriteExecutor writer,
        ICurrentGuest currentGuest,
        MusicCatalogService catalog,
        TimeProvider timeProvider,
        RoomQueueSettings settings,
        IRoomRealtimePublisher realtime,
        ILogger<RoomQueueApplicationService> logger)
    {
        _rooms = rooms ?? throw new ArgumentNullException(nameof(rooms));
        _queues = queues ?? throw new ArgumentNullException(nameof(queues));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _currentGuest = currentGuest
            ?? throw new ArgumentNullException(nameof(currentGuest));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
        _settings = settings
            ?? throw new ArgumentNullException(nameof(settings));
        _realtime = realtime
            ?? throw new ArgumentNullException(nameof(realtime));
        _logger = logger
            ?? throw new ArgumentNullException(nameof(logger));

        if (_settings.MaxPendingItems is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(settings),
                "The maximum pending queue size must be between 1 and 100.");
        }
    }

    public async Task<RoomQueueDetails> GetQueueAsync(
        Guid roomId,
        CancellationToken cancellationToken = default)
    {
        var guestId = RequireGuest();
        ValidateRoomId(roomId);

        var room = await _rooms.GetByIdAsync(
            roomId,
            cancellationToken);

        RequireMemberRole(room, guestId);

        var session = await _queues.GetByRoomIdAsync(
            roomId,
            cancellationToken);

        return MapQueue(roomId, session);
    }

    public async Task<RoomQueueDetails> AddTrackAsync(
        Guid roomId,
        TrackReference reference,
        CancellationToken cancellationToken = default)
    {
        var guestId = RequireGuest();
        ValidateRoomId(roomId);

        if (reference is null ||
            string.IsNullOrWhiteSpace(reference.Provider) ||
            string.IsNullOrWhiteSpace(reference.TrackId))
        {
            throw Error(
                ApplicationErrorCode.Validation,
                "A valid track reference is required.");
        }

        // Avoid an external Audius request for obvious non-members.
        // This is only a preliminary check; authorization is
        // repeated under the PostgreSQL Room lock.
        var preliminaryRoom = await _rooms.GetByIdAsync(
            roomId,
            cancellationToken);

        RequireMemberRole(preliminaryRoom, guestId);

        var musicTrack = await _catalog.GetTrackAsync(
            reference,
            cancellationToken);

        if (musicTrack is null)
        {
            throw Error(
                ApplicationErrorCode.TrackNotFound,
                "The requested track does not exist.");
        }

        if (musicTrack.DurationMs is not > 0)
        {
            throw Error(
                ApplicationErrorCode.TrackNotEligible,
                "The track has no valid duration.");
        }

        QueuedTrack queuedTrack;

        try
        {
            queuedTrack = new QueuedTrack(
                musicTrack.Reference,
                musicTrack.Title,
                musicTrack.Artists,
                musicTrack.DurationMs.Value,
                musicTrack.ArtworkUrl,
                musicTrack.ExternalUrl);
        }
        catch (ArgumentException exception)
        {
            throw new RoomApplicationException(
                ApplicationErrorCode.TrackNotEligible,
                "The track metadata cannot be stored in the queue.",
                exception);
        }

        RoomQueueDetails committed;

        try
        {
            committed = await _writer.ExecuteAsync(
                roomId,
                async (room, ct) =>
                {
                    RequireMemberRole(room, guestId);

                    var session = await _queues.GetByRoomIdAsync(
                        roomId,
                        ct);

                    if (session is null)
                    {
                        session = RoomMusicSession.Create(
                            roomId,
                            _settings.MaxPendingItems);

                        await _queues.AddAsync(session, ct);
                    }

                    try
                    {
                        session.AddTrack(
                            queuedTrack,
                            guestId,
                            _timeProvider.GetUtcNow());
                    }
                    catch (RoomQueueDomainException exception)
                    {
                        throw MapDomainError(exception);
                    }

                    return MapQueue(roomId, session);
                },
                cancellationToken);
        }
        catch (RoomQueueConcurrencyException exception)
        {
            throw MapConcurrencyError(exception);
        }

        await TryPublishAsync(committed);
        return committed;
    }

    public async Task<RoomQueueDetails> RemoveTrackAsync(
        Guid roomId,
        Guid queueItemId,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        var guestId = RequireGuest();
        ValidateRoomId(roomId);
        ValidateItemId(queueItemId);
        ValidateExpectedVersion(expectedVersion);

        RoomQueueDetails committed;

        try
        {
            committed = await _writer.ExecuteAsync(
                roomId,
                async (room, ct) =>
                {
                    var role = RequireMemberRole(room, guestId);

                    var session = await _queues.GetByRoomIdAsync(
                        roomId,
                        ct);

                    EnsureVersion(
                        expectedVersion,
                        session?.Version ?? 0);

                    if (session is null)
                    {
                        throw Error(
                            ApplicationErrorCode.QueueItemNotFound,
                            "Queue item was not found.");
                    }

                    try
                    {
                        session.RemoveTrack(
                            queueItemId,
                            guestId,
                            role);
                    }
                    catch (RoomQueueDomainException exception)
                    {
                        throw MapDomainError(exception);
                    }

                    return MapQueue(roomId, session);
                },
                cancellationToken);
        }
        catch (RoomQueueConcurrencyException exception)
        {
            throw MapConcurrencyError(exception);
        }

        await TryPublishAsync(committed);
        return committed;
    }

    public async Task<RoomQueueDetails> MoveTrackAsync(
        Guid roomId,
        Guid queueItemId,
        int position,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        var guestId = RequireGuest();
        ValidateRoomId(roomId);
        ValidateItemId(queueItemId);
        ValidateExpectedVersion(expectedVersion);

        if (position < 0)
        {
            throw Error(
                ApplicationErrorCode.InvalidQueuePosition,
                "Position cannot be negative.");
        }

        var changed = false;
        RoomQueueDetails committed;

        try
        {
            committed = await _writer.ExecuteAsync(
                roomId,
                async (room, ct) =>
                {
                    var role = RequireMemberRole(room, guestId);

                    if (role != RoomRole.Owner)
                    {
                        throw Error(
                            ApplicationErrorCode.AccessDenied,
                            "Only the room owner can reorder tracks.");
                    }

                    var session = await _queues.GetByRoomIdAsync(
                        roomId,
                        ct);

                    EnsureVersion(
                        expectedVersion,
                        session?.Version ?? 0);

                    if (session is null)
                    {
                        throw Error(
                            ApplicationErrorCode.QueueItemNotFound,
                            "Queue item was not found.");
                    }

                    try
                    {
                        changed = session.MoveTrack(
                            queueItemId,
                            position,
                            role);
                    }
                    catch (RoomQueueDomainException exception)
                    {
                        throw MapDomainError(exception);
                    }

                    return MapQueue(roomId, session);
                },
                cancellationToken);
        }
        catch (RoomQueueConcurrencyException exception)
        {
            throw MapConcurrencyError(exception);
        }

        if (changed)
        {
            await TryPublishAsync(committed);
        }

        return committed;
    }

    private async Task TryPublishAsync(
        RoomQueueDetails snapshot)
    {
        try
        {
            using var timeout = new CancellationTokenSource(
                PublishTimeout);

            await _realtime.QueueChangedAsync(
                snapshot,
                timeout.Token);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Queue version {Version} was committed for room " +
                "{RoomId}, but QueueChanged delivery failed.",
                snapshot.Version,
                snapshot.RoomId);
        }
    }

    private Guid RequireGuest()
    {
        var guestId = _currentGuest.GuestId;

        if (guestId is null || guestId == Guid.Empty)
        {
            throw Error(
                ApplicationErrorCode.GuestSessionRequired,
                "An active guest session is required.");
        }

        return guestId.Value;
    }

    private static RoomRole RequireMemberRole(
        Room? room,
        Guid guestId)
    {
        if (room is null || room.Status != RoomStatus.Active)
        {
            throw Error(
                ApplicationErrorCode.RoomNotFound,
                "Room was not found.");
        }

        var role = room.GetRole(guestId);

        if (role is not (RoomRole.Owner or RoomRole.Member))
        {
            throw Error(
                ApplicationErrorCode.RoomNotFound,
                "Room was not found.");
        }

        return role.Value;
    }

    private static void ValidateRoomId(Guid roomId)
    {
        if (roomId == Guid.Empty)
        {
            throw Error(
                ApplicationErrorCode.Validation,
                "Room ID cannot be empty.");
        }
    }

    private static void ValidateItemId(Guid itemId)
    {
        if (itemId == Guid.Empty)
        {
            throw Error(
                ApplicationErrorCode.Validation,
                "Queue item ID cannot be empty.");
        }
    }

    private static void ValidateExpectedVersion(long version)
    {
        if (version < 0)
        {
            throw Error(
                ApplicationErrorCode.Validation,
                "Queue version cannot be negative.");
        }
    }

    private static void EnsureVersion(
        long expectedVersion,
        long actualVersion)
    {
        if (expectedVersion != actualVersion)
        {
            throw Error(
                ApplicationErrorCode.QueueVersionMismatch,
                "The queue has changed. Reload it before retrying.");
        }
    }

    private RoomQueueDetails MapQueue(
        Guid roomId,
        RoomMusicSession? session)
    {
        if (session is null)
        {
            return new RoomQueueDetails(
                roomId,
                0,
                _settings.MaxPendingItems,
                []);
        }

        var items = session.PendingItems
            .OrderBy(item => item.Position)
            .Select(item => new QueueItemDetails(
                item.Id,
                item.Position,
                new QueuedTrackDetails(
                    item.Track.Reference,
                    item.Track.Title,
                    item.Track.Artists,
                    item.Track.DurationMs,
                    item.Track.ArtworkUrl,
                    item.Track.ExternalUrl),
                item.AddedByGuestId,
                item.AddedAtUtc))
            .ToArray();

        return new RoomQueueDetails(
            session.RoomId,
            session.Version,
            session.MaxPendingItems,
            items);
    }

    private static RoomApplicationException MapDomainError(
        RoomQueueDomainException exception)
    {
        var code = exception.ErrorCode switch
        {
            RoomQueueDomainErrorCode.QueueFull =>
                ApplicationErrorCode.QueueFull,

            RoomQueueDomainErrorCode.QueueItemNotFound =>
                ApplicationErrorCode.QueueItemNotFound,

            RoomQueueDomainErrorCode.AccessDenied =>
                ApplicationErrorCode.AccessDenied,

            RoomQueueDomainErrorCode.InvalidQueuePosition =>
                ApplicationErrorCode.InvalidQueuePosition,

            _ => ApplicationErrorCode.Validation
        };

        return new RoomApplicationException(
            code,
            exception.Message,
            exception);
    }

    private static RoomApplicationException MapConcurrencyError(
        Exception exception)
    {
        return new RoomApplicationException(
            ApplicationErrorCode.ConcurrencyConflict,
            "The queue was changed concurrently. Reload and retry.",
            exception);
    }

    private static RoomApplicationException Error(
        ApplicationErrorCode code,
        string message)
    {
        return new RoomApplicationException(code, message);
    }
}