using Microsoft.Extensions.Logging;
using VibyRoom.Application.Abstractions.Authentication;
using VibyRoom.Application.Abstractions.Exceptions;
using VibyRoom.Application.Abstractions.Music;
using VibyRoom.Application.Abstractions.Repositories;
using VibyRoom.Application.Abstractions.Realtime;
using VibyRoom.Application.Configuration;
using VibyRoom.Application.Contracts.Playback;
using VibyRoom.Application.Contracts.Queue;
using VibyRoom.Application.Errors;
using VibyRoom.Application.Music;
using VibyRoom.Application.Playback;
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
    private readonly IMusicStreamProvider _streamProvider;
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
        IMusicStreamProvider streamProvider,
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
        _catalog = catalog
            ?? throw new ArgumentNullException(nameof(catalog));
        _streamProvider = streamProvider
            ?? throw new ArgumentNullException(nameof(streamProvider));
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
                "Maximum pending queue size must be between 1 and 100.");
        }
    }

    public async Task<RoomQueueDetails> GetQueueAsync(
        Guid roomId,
        CancellationToken cancellationToken = default)
    {
        var guestId = RequireGuest();
        ValidateRoomId(roomId);

        var room = await _rooms.GetByIdAsync(
            roomId, cancellationToken);

        RequireMemberRole(room, guestId);

        var session = await _queues.GetByRoomIdAsync(
            roomId, cancellationToken);

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

        // Проверка до запроса к Audius.
        var preliminaryRoom = await _rooms.GetByIdAsync(
            roomId, cancellationToken);

        RequireMemberRole(preliminaryRoom, guestId);

        var musicTrack = await _catalog.GetTrackAsync(
            reference, cancellationToken);

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
        
        bool canStream;

        try
        {
            canStream = await _streamProvider.CanStreamAsync(
                musicTrack.Reference,
                cancellationToken);
        }
        catch (MusicProviderException exception)
        {
            var code = exception.FailureKind switch
            {
                MusicProviderFailureKind.AccessDenied =>
                    ApplicationErrorCode.StreamAccessDenied,

                MusicProviderFailureKind.RateLimited =>
                    ApplicationErrorCode.MusicProviderRateLimited,

                _ => ApplicationErrorCode.MusicProviderUnavailable
            };

            throw new RoomApplicationException(
                code,
                "Unable to verify Audius streaming availability.",
                exception);
        }

        if (!canStream)
        {
            throw Error(
                ApplicationErrorCode.TrackNotStreamable,
                "This track cannot be streamed publicly.");
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

        (
            RoomQueueDetails Queue,
            RoomMusicStateDetails Music
        ) committed;

        try
        {
            committed = await _writer.ExecuteAsync<(
                RoomQueueDetails Queue,
                RoomMusicStateDetails Music)>(
                roomId,
                async (room, ct) =>
                {
                    RequireMemberRole(room, guestId);

                    var session = await _queues.GetByRoomIdAsync(
                        roomId, ct);

                    if (session is null)
                    {
                        session = RoomMusicSession.Create(
                            roomId,
                            _settings.MaxPendingItems);

                        await _queues.AddAsync(session, ct);
                    }

                    var now = _timeProvider.GetUtcNow();

                    try
                    {
                        session.AdvanceIfDue(now);

                        session.AddTrack(
                            queuedTrack,
                            guestId,
                            now);
                    }
                    catch (RoomQueueDomainException exception)
                    {
                        throw MapDomainError(exception);
                    }

                    return (
                        Queue: MapQueue(roomId, session),
                        Music: RoomMusicStateMapper.ToDetails(
                            roomId, session, now)
                    );
                },
                cancellationToken);
        }
        catch (RoomQueueConcurrencyException exception)
        {
            throw MapConcurrencyError(exception);
        }
        catch (RoomConcurrencyException exception)
        {
            throw MapConcurrencyError(exception);
        }

        await PublishCommittedAsync(
            committed.Queue,
            committed.Music,
            queueChanged: true);

        return committed.Queue;
    }

    public Task<RoomQueueDetails> RemoveTrackAsync(
        Guid roomId,
        Guid queueItemId,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ValidateItemId(queueItemId);

        return ExecuteQueueMutationAsync(
            roomId,
            expectedVersion,
            ownerOnly: false,
            (session, role, guestId) =>
            {
                session.RemoveTrack(
                    queueItemId,
                    guestId,
                    role);
            },
            cancellationToken);
    }

    public Task<RoomQueueDetails> MoveTrackAsync(
        Guid roomId,
        Guid queueItemId,
        int position,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ValidateItemId(queueItemId);

        if (position < 0)
        {
            throw Error(
                ApplicationErrorCode.InvalidQueuePosition,
                "Position cannot be negative.");
        }

        return ExecuteQueueMutationAsync(
            roomId,
            expectedVersion,
            ownerOnly: true,
            (session, role, _) =>
            {
                session.MoveTrack(
                    queueItemId,
                    position,
                    role);
            },
            cancellationToken);
    }

    private async Task<RoomQueueDetails> ExecuteQueueMutationAsync(
        Guid roomId,
        long expectedVersion,
        bool ownerOnly,
        Action<RoomMusicSession, RoomRole, Guid> operation,
        CancellationToken cancellationToken)
    {
        var guestId = RequireGuest();
        ValidateRoomId(roomId);
        ValidateExpectedVersion(expectedVersion);

        ArgumentNullException.ThrowIfNull(operation);

        (
            RoomQueueDetails Queue,
            RoomMusicStateDetails Music,
            bool Changed,
            bool QueueChanged
        ) committed;

        try
        {
            committed = await _writer.ExecuteAsync<(
                RoomQueueDetails Queue,
                RoomMusicStateDetails Music,
                bool Changed,
                bool QueueChanged)>(
                roomId,
                async (room, ct) =>
                {
                    var role = RequireMemberRole(room, guestId);

                    if (ownerOnly && role != RoomRole.Owner)
                    {
                        throw Error(
                            ApplicationErrorCode.AccessDenied,
                            "Only the room owner can reorder tracks.");
                    }

                    var session = await _queues.GetByRoomIdAsync(
                        roomId, ct);

                    var previousVersion = session?.Version ?? 0;

                    var previousIds = session?.PendingItems
                        .Select(item => item.Id)
                        .ToArray()
                        ?? Array.Empty<Guid>();

                    var now = _timeProvider.GetUtcNow();

                    try
                    {
                        session?.AdvanceIfDue(now);
                    }
                    catch (RoomQueueDomainException exception)
                    {
                        throw MapDomainError(exception);
                    }

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
                        operation(session, role, guestId);
                    }
                    catch (RoomQueueDomainException exception)
                    {
                        throw MapDomainError(exception);
                    }

                    var currentIds = session.PendingItems
                        .Select(item => item.Id)
                        .ToArray();

                    return (
                        Queue: MapQueue(roomId, session),
                        Music: RoomMusicStateMapper.ToDetails(
                            roomId, session, now),
                        Changed: session.Version != previousVersion,
                        QueueChanged: !previousIds.SequenceEqual(
                            currentIds)
                    );
                },
                cancellationToken);
        }
        catch (RoomQueueConcurrencyException exception)
        {
            throw MapConcurrencyError(exception);
        }
        catch (RoomConcurrencyException exception)
        {
            throw MapConcurrencyError(exception);
        }

        if (committed.Changed)
        {
            await PublishCommittedAsync(
                committed.Queue,
                committed.Music,
                committed.QueueChanged);
        }

        return committed.Queue;
    }

    private async Task PublishCommittedAsync(
        RoomQueueDetails queue,
        RoomMusicStateDetails music,
        bool queueChanged)
    {
        if (queue.RoomId != music.RoomId ||
            queue.Version != music.Version ||
            music.Playback.Version != music.Version)
        {
            throw new InvalidOperationException(
                "Music and queue snapshots have different versions.");
        }

        if (queueChanged)
        {
            try
            {
                using var timeout =
                    new CancellationTokenSource(PublishTimeout);

                await _realtime.QueueChangedAsync(
                    queue,
                    timeout.Token);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "QueueChanged delivery failed for room {RoomId}.",
                    queue.RoomId);
            }
        }

        try
        {
            using var timeout =
                new CancellationTokenSource(PublishTimeout);

            await _realtime.MusicStateChangedAsync(
                music,
                timeout.Token);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "MusicStateChanged delivery failed for room {RoomId}.",
                music.RoomId);
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
                "The music state has changed. Reload before retrying.");
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

            RoomQueueDomainErrorCode.QueueEmpty =>
                ApplicationErrorCode.QueueEmpty,

            RoomQueueDomainErrorCode.InvalidPlaybackState =>
                ApplicationErrorCode.InvalidPlaybackState,

            RoomQueueDomainErrorCode.InvalidPlaybackPosition =>
                ApplicationErrorCode.InvalidPlaybackPosition,

            RoomQueueDomainErrorCode.PlaybackNotActive =>
                ApplicationErrorCode.PlaybackNotActive,

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
            "The music state was changed concurrently. Reload and retry.",
            exception);
    }

    private static RoomApplicationException Error(
        ApplicationErrorCode code,
        string message)
    {
        return new RoomApplicationException(code, message);
    }
}