using Microsoft.Extensions.Logging;
using VibyRoom.Application.Abstractions.Authentication;
using VibyRoom.Application.Abstractions.Exceptions;
using VibyRoom.Application.Abstractions.Repositories;
using VibyRoom.Application.Abstractions.Realtime;
using VibyRoom.Application.Configuration;
using VibyRoom.Application.Contracts.Playback;
using VibyRoom.Application.Contracts.Queue;
using VibyRoom.Application.Errors;
using VibyRoom.Domain.Core.Entities.Rooms;
using VibyRoom.Domain.Core.Entities.Rooms.Enums;
using VibyRoom.Domain.Music.Queue;
using VibyRoom.Domain.Music.Queue.Exceptions;

namespace VibyRoom.Application.Playback;

public sealed class RoomPlaybackApplicationService
{
    private static readonly TimeSpan PublishTimeout =
        TimeSpan.FromSeconds(5);

    private readonly IRoomMusicSessionRepository _sessions;
    private readonly IRoomQueueWriteExecutor _writer;
    private readonly ICurrentGuest _currentGuest;
    private readonly TimeProvider _timeProvider;
    private readonly RoomQueueSettings _settings;
    private readonly IRoomRealtimePublisher _realtime;
    private readonly ILogger<RoomPlaybackApplicationService> _logger;

    public RoomPlaybackApplicationService(
        IRoomMusicSessionRepository sessions,
        IRoomQueueWriteExecutor writer,
        ICurrentGuest currentGuest,
        TimeProvider timeProvider,
        RoomQueueSettings settings,
        IRoomRealtimePublisher realtime,
        ILogger<RoomPlaybackApplicationService> logger)
    {
        _sessions = sessions
            ?? throw new ArgumentNullException(nameof(sessions));

        _writer = writer
            ?? throw new ArgumentNullException(nameof(writer));

        _currentGuest = currentGuest
            ?? throw new ArgumentNullException(nameof(currentGuest));

        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));

        _settings = settings
            ?? throw new ArgumentNullException(nameof(settings));

        _realtime = realtime
            ?? throw new ArgumentNullException(nameof(realtime));

        _logger = logger
            ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PlaybackDetails> GetPlaybackAsync(
        Guid roomId,
        CancellationToken cancellationToken = default)
    {
        var state = await GetMusicStateAsync(
            roomId,
            cancellationToken);

        return state.Playback;
    }

    public Task<RoomMusicStateDetails> GetMusicStateAsync(
        Guid roomId,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            roomId,
            RequireGuest(),
            ownerOnly: false,
            expectedVersion: null,
            operation: null,
            missingSessionCode: ApplicationErrorCode.PlaybackNotActive,
            cancellationToken);
    }

    public Task<RoomMusicStateDetails> PlayAsync(
        Guid roomId,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        return ExecuteCommandAsync(
            roomId,
            expectedVersion,
            ApplicationErrorCode.QueueEmpty,
            static (session, now) => session.Play(now),
            cancellationToken);
    }

    public Task<RoomMusicStateDetails> PauseAsync(
        Guid roomId,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        return ExecuteCommandAsync(
            roomId,
            expectedVersion,
            ApplicationErrorCode.PlaybackNotActive,
            static (session, now) => session.Pause(now),
            cancellationToken);
    }

    public Task<RoomMusicStateDetails> ResumeAsync(
        Guid roomId,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        return ExecuteCommandAsync(
            roomId,
            expectedVersion,
            ApplicationErrorCode.PlaybackNotActive,
            static (session, now) => session.Resume(now),
            cancellationToken);
    }

    public Task<RoomMusicStateDetails> SeekAsync(
        Guid roomId,
        long positionMs,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        if (positionMs < 0)
        {
            throw Error(
                ApplicationErrorCode.InvalidPlaybackPosition,
                "Playback position cannot be negative.");
        }

        return ExecuteCommandAsync(
            roomId,
            expectedVersion,
            ApplicationErrorCode.PlaybackNotActive,
            (session, now) =>
            {
                session.Seek(positionMs, now);
            },
            cancellationToken);
    }

    public Task<RoomMusicStateDetails> SkipAsync(
        Guid roomId,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        return ExecuteCommandAsync(
            roomId,
            expectedVersion,
            ApplicationErrorCode.PlaybackNotActive,
            static (session, now) => session.Skip(now),
            cancellationToken);
    }

    public async Task<bool> AdvanceRoomIfDueAsync(
        Guid roomId,
        CancellationToken cancellationToken = default)
    {
        var result = await ExecuteCoreAsync(
            roomId,
            actorGuestId: null,
            ownerOnly: false,
            expectedVersion: null,
            operation: null,
            missingSessionCode: ApplicationErrorCode.PlaybackNotActive,
            cancellationToken);

        return result.Changed;
    }

    private Task<RoomMusicStateDetails> ExecuteCommandAsync(
        Guid roomId,
        long expectedVersion,
        ApplicationErrorCode missingSessionCode,
        Action<RoomMusicSession, DateTimeOffset> operation,
        CancellationToken cancellationToken)
    {
        if (expectedVersion < 0)
        {
            throw Error(
                ApplicationErrorCode.Validation,
                "Music state version cannot be negative.");
        }

        return ExecuteAsync(
            roomId,
            RequireGuest(),
            ownerOnly: true,
            expectedVersion,
            operation,
            missingSessionCode,
            cancellationToken);
    }

    private async Task<RoomMusicStateDetails> ExecuteAsync(
        Guid roomId,
        Guid? actorGuestId,
        bool ownerOnly,
        long? expectedVersion,
        Action<RoomMusicSession, DateTimeOffset>? operation,
        ApplicationErrorCode missingSessionCode,
        CancellationToken cancellationToken)
    {
        var result = await ExecuteCoreAsync(
            roomId,
            actorGuestId,
            ownerOnly,
            expectedVersion,
            operation,
            missingSessionCode,
            cancellationToken);

        return result.Snapshot;
    }

    private async Task<(
        RoomMusicStateDetails Snapshot,
        bool Changed,
        bool QueueChanged)> ExecuteCoreAsync(
        Guid roomId,
        Guid? actorGuestId,
        bool ownerOnly,
        long? expectedVersion,
        Action<RoomMusicSession, DateTimeOffset>? operation,
        ApplicationErrorCode missingSessionCode,
        CancellationToken cancellationToken)
    {
        if (roomId == Guid.Empty)
        {
            throw Error(
                ApplicationErrorCode.Validation,
                "Room ID cannot be empty.");
        }

        (
            RoomMusicStateDetails Snapshot,
            bool Changed,
            bool QueueChanged
        ) committed;

        try
        {
            committed = await _writer.ExecuteAsync<(
                RoomMusicStateDetails Snapshot,
                bool Changed,
                bool QueueChanged)>(
                roomId,
                async (room, ct) =>
                {
                    EnsureAccess(room, actorGuestId, ownerOnly);

                    var session = await _sessions.GetByRoomIdAsync(
                        roomId,
                        ct);

                    var now = _timeProvider.GetUtcNow();

                    var previousVersion = session?.Version ?? 0;

                    // Фиксируем состав и порядок ожидающей очереди.
                    var previousQueueItemIds =
                        session?.PendingItems
                            .Select(item => item.Id)
                            .ToArray()
                        ?? Array.Empty<Guid>();

                    try
                    {
                        session?.AdvanceIfDue(now);
                    }
                    catch (RoomQueueDomainException exception)
                    {
                        throw MapDomainError(exception);
                    }

                    var actualVersion = session?.Version ?? 0;

                    if (expectedVersion.HasValue &&
                        expectedVersion.Value != actualVersion)
                    {
                        throw Error(
                            ApplicationErrorCode.QueueVersionMismatch,
                            "Music state has changed. Reload and retry.");
                    }

                    if (operation is not null)
                    {
                        if (session is null)
                        {
                            throw Error(
                                missingSessionCode,
                                "Music playback is not available.");
                        }

                        try
                        {
                            operation(session, now);
                        }
                        catch (RoomQueueDomainException exception)
                        {
                            throw MapDomainError(exception);
                        }
                    }

                    var newVersion = session?.Version ?? 0;

                    var changed = newVersion != previousVersion;

                    var currentQueueItemIds =
                        session?.PendingItems
                            .Select(item => item.Id)
                            .ToArray()
                        ?? Array.Empty<Guid>();

                    // Сравниваем ID в порядке очереди,
                    // а не только количество элементов.
                    var queueChanged =
                        !previousQueueItemIds.SequenceEqual(
                            currentQueueItemIds);

                    var snapshot = RoomMusicStateMapper.ToDetails(
                        roomId,
                        session,
                        now);

                    return (
                        Snapshot: snapshot,
                        Changed: changed,
                        QueueChanged: queueChanged
                    );
                },
                cancellationToken);
        }
        catch (RoomQueueConcurrencyException exception)
        {
            throw MapConcurrency(exception);
        }
        catch (RoomConcurrencyException exception)
        {
            throw MapConcurrency(exception);
        }

        // Транзакция завершена внутри RoomQueueWriteExecutor.
        // Только теперь отправляем SignalR.
        if (committed.Changed)
        {
            await PublishCommittedStateAsync(
                committed.Snapshot,
                committed.QueueChanged);
        }

        return committed;
    }

    private async Task PublishCommittedStateAsync(
        RoomMusicStateDetails snapshot,
        bool queueChanged)
    {
        if (snapshot.Version != snapshot.Playback.Version)
        {
            throw new InvalidOperationException(
                "Playback and music state versions do not match.");
        }

        if (queueChanged)
        {
            try
            {
                using var timeout =
                    new CancellationTokenSource(PublishTimeout);

                var queue = new RoomQueueDetails(
                    snapshot.RoomId,
                    snapshot.Version,
                    _settings.MaxPendingItems,
                    snapshot.Queue);

                await _realtime.QueueChangedAsync(
                    queue,
                    timeout.Token);
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "QueueChanged delivery failed for room {RoomId}.",
                    snapshot.RoomId);
            }
        }

        try
        {
            using var timeout =
                new CancellationTokenSource(PublishTimeout);

            await _realtime.MusicStateChangedAsync(
                snapshot,
                timeout.Token);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "MusicStateChanged delivery failed for room {RoomId}.",
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

    private static void EnsureAccess(
        Room? room,
        Guid? actorGuestId,
        bool ownerOnly)
    {
        if (room is null || room.Status != RoomStatus.Active)
        {
            throw Error(
                ApplicationErrorCode.RoomNotFound,
                "Room was not found.");
        }

        // Системная операция фонового worker.
        if (!actorGuestId.HasValue)
        {
            return;
        }

        var role = room.GetRole(actorGuestId.Value);

        if (role is not (RoomRole.Owner or RoomRole.Member))
        {
            throw Error(
                ApplicationErrorCode.RoomNotFound,
                "Room was not found.");
        }

        if (ownerOnly && role != RoomRole.Owner)
        {
            throw Error(
                ApplicationErrorCode.AccessDenied,
                "Only the room owner can control playback.");
        }
    }

    private static RoomApplicationException MapDomainError(
        RoomQueueDomainException exception)
    {
        var code = exception.ErrorCode switch
        {
            RoomQueueDomainErrorCode.QueueEmpty =>
                ApplicationErrorCode.QueueEmpty,

            RoomQueueDomainErrorCode.InvalidPlaybackState =>
                ApplicationErrorCode.InvalidPlaybackState,

            RoomQueueDomainErrorCode.InvalidPlaybackPosition =>
                ApplicationErrorCode.InvalidPlaybackPosition,

            RoomQueueDomainErrorCode.PlaybackNotActive =>
                ApplicationErrorCode.PlaybackNotActive,

            RoomQueueDomainErrorCode.AccessDenied =>
                ApplicationErrorCode.AccessDenied,

            _ => ApplicationErrorCode.Validation
        };

        return new RoomApplicationException(
            code,
            exception.Message,
            exception);
    }

    private static RoomApplicationException MapConcurrency(
        Exception exception)
    {
        return new RoomApplicationException(
            ApplicationErrorCode.ConcurrencyConflict,
            "Music state was changed concurrently. Reload and retry.",
            exception);
    }

    private static RoomApplicationException Error(
        ApplicationErrorCode code,
        string message)
    {
        return new RoomApplicationException(code, message);
    }
}