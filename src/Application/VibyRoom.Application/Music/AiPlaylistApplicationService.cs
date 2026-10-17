using Microsoft.Extensions.Logging;
using VibyRoom.Application.Abstractions.Authentication;
using VibyRoom.Application.Abstractions.Exceptions;
using VibyRoom.Application.Abstractions.Repositories;
using VibyRoom.Application.Abstractions.Realtime;
using VibyRoom.Application.Configuration;
using VibyRoom.Application.Contracts.Playback;
using VibyRoom.Application.Contracts.Queue;
using VibyRoom.Application.Errors;
using VibyRoom.Application.Playback;
using VibyRoom.Domain.Core.Entities.Rooms;
using VibyRoom.Domain.Core.Entities.Rooms.Enums;
using VibyRoom.Domain.Music.Queue;
using VibyRoom.Domain.Music.Queue.Exceptions;

namespace VibyRoom.Application.Music;

public sealed class AiPlaylistApplicationService
{
    private readonly IRoomRepository _rooms;
    private readonly IRoomMusicSessionRepository _sessions;
    private readonly IRoomQueueWriteExecutor _writer;
    private readonly ICurrentGuest _currentGuest;
    private readonly AiTrackPicker _picker;
    private readonly RoomQueueSettings _settings;
    private readonly TimeProvider _timeProvider;
    private readonly IRoomRealtimePublisher _realtime;
    private readonly ILogger<AiPlaylistApplicationService> _logger;

    public AiPlaylistApplicationService(
        IRoomRepository rooms,
        IRoomMusicSessionRepository sessions,
        IRoomQueueWriteExecutor writer,
        ICurrentGuest currentGuest,
        AiTrackPicker picker,
        RoomQueueSettings settings,
        TimeProvider timeProvider,
        IRoomRealtimePublisher realtime,
        ILogger<AiPlaylistApplicationService> logger)
    {
        _rooms = rooms ?? throw new ArgumentNullException(nameof(rooms));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _currentGuest = currentGuest
            ?? throw new ArgumentNullException(nameof(currentGuest));
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));
        _realtime = realtime
            ?? throw new ArgumentNullException(nameof(realtime));
        _logger = logger
            ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<RoomMusicStateDetails> GenerateAsync(
        Guid roomId,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        var normalized = prompt?.Trim();

        if (normalized is null ||
            normalized.Length is < 3 or > 500)
        {
            throw Error(
                ApplicationErrorCode.Validation,
                "AI prompt must contain between 3 and 500 characters.");
        }

        return ExecuteAsync(
            roomId,
            normalized,
            more: false,
            cancellationToken);
    }

    public Task<RoomMusicStateDetails> MoreAsync(
        Guid roomId,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            roomId,
            prompt: null,
            more: true,
            cancellationToken);
    }

    public async Task<string?> GetPromptAsync(
        Guid roomId,
        CancellationToken cancellationToken = default)
    {
        var guestId = RequireGuest();

        var room = await _rooms.GetByIdAsync(
            roomId,
            cancellationToken);

        RequireOwner(room, guestId);

        var session = await _sessions.GetByRoomIdAsync(
            roomId,
            cancellationToken);

        return session?.AiPrompt;
    }

    private async Task<RoomMusicStateDetails> ExecuteAsync(
        Guid roomId,
        string? prompt,
        bool more,
        CancellationToken cancellationToken)
    {
        var guestId = RequireGuest();

        if (roomId == Guid.Empty)
        {
            throw Error(
                ApplicationErrorCode.Validation,
                "Room ID cannot be empty.");
        }

        var room = await _rooms.GetByIdAsync(
            roomId,
            cancellationToken);

        RequireOwner(room, guestId);

        var existing = await _sessions.GetByRoomIdAsync(
            roomId,
            cancellationToken);

        var effectivePrompt = more
            ? existing?.AiPrompt
            : prompt;

        if (string.IsNullOrWhiteSpace(effectivePrompt))
        {
            throw Error(
                ApplicationErrorCode.AiNotInitialized,
                "Generate a playlist before requesting more tracks.");
        }

        var expectedVersion = existing?.Version ?? 0;

        if (existing is not null &&
            existing.PendingItems.Count >
            existing.MaxPendingItems - AiTrackPicker.BatchSize)
        {
            throw Error(
                ApplicationErrorCode.QueueFull,
                "Not enough queue capacity for 15 AI tracks.");
        }

        var excluded = new HashSet<string>(
            StringComparer.Ordinal);

        if (more && existing is not null)
        {
            excluded.UnionWith(existing.AiTrackKeys);
        }

        if (existing is not null)
        {
            foreach (var item in existing.PendingItems)
            {
                excluded.Add(
                    $"{item.Track.Reference.Provider}:{item.Track.Reference.TrackId}");
            }

            if (existing.Playback.CurrentItem is { } current)
            {
                excluded.Add(
                    $"{current.Track.Reference.Provider}:{current.Track.Reference.TrackId}");
            }
        }

        var round = more && existing is not null
            ? existing.AiTrackKeys.Count / AiTrackPicker.BatchSize + 1
            : 1;

        var selected = await _picker.PickAsync(
            effectivePrompt,
            excluded,
            round,
            cancellationToken);

        RoomMusicStateDetails committed;

        try
        {
            committed = await _writer.ExecuteAsync(
                roomId,
                async (lockedRoom, ct) =>
                {
                    RequireOwner(lockedRoom, guestId);

                    var session = await _sessions.GetByRoomIdAsync(
                        roomId,
                        ct);

                    var actualVersion = session?.Version ?? 0;

                    if (actualVersion != expectedVersion)
                    {
                        throw Error(
                            ApplicationErrorCode.ConcurrencyConflict,
                            "Room music state changed during AI generation.");
                    }

                    if (more &&
                        !string.Equals(
                            session?.AiPrompt,
                            effectivePrompt,
                            StringComparison.Ordinal))
                    {
                        throw Error(
                            ApplicationErrorCode.ConcurrencyConflict,
                            "AI playlist context has changed.");
                    }

                    if (session is null)
                    {
                        session = RoomMusicSession.Create(
                            roomId,
                            _settings.MaxPendingItems);

                        await _sessions.AddAsync(session, ct);
                    }

                    var now = _timeProvider.GetUtcNow();

                    try
                    {
                        session.AdvanceIfDue(now);

                        if (session.PendingItems.Count >
                            session.MaxPendingItems - AiTrackPicker.BatchSize)
                        {
                            throw Error(
                                ApplicationErrorCode.QueueFull,
                                "Not enough queue capacity for 15 AI tracks.");
                        }

                        foreach (var track in selected)
                        {
                            session.AddTrack(
                                track,
                                guestId,
                                now);
                        }

                        session.RecordAiSelection(
                            effectivePrompt,
                            selected,
                            resetHistory: !more);
                    }
                    catch (RoomQueueDomainException exception)
                    {
                        throw new RoomApplicationException(
                            ApplicationErrorCode.QueueFull,
                            "Unable to append the AI playlist.",
                            exception);
                    }

                    return RoomMusicStateMapper.ToDetails(
                        roomId,
                        session,
                        now);
                },
                cancellationToken);
        }
        catch (Exception exception)
            when (exception is RoomQueueConcurrencyException
                or RoomConcurrencyException)
        {
            throw new RoomApplicationException(
                ApplicationErrorCode.ConcurrencyConflict,
                "AI playlist could not be committed concurrently.",
                exception);
        }

        await PublishAsync(committed);

        return committed;
    }

    private async Task PublishAsync(RoomMusicStateDetails state)
    {
        var queue = new RoomQueueDetails(
            state.RoomId,
            state.Version,
            _settings.MaxPendingItems,
            state.Queue);

        try
        {
            using var timeout =
                new CancellationTokenSource(TimeSpan.FromSeconds(5));

            await _realtime.QueueChangedAsync(
                queue,
                timeout.Token);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "AI queue publication failed for room {RoomId}.",
                state.RoomId);
        }

        try
        {
            using var timeout =
                new CancellationTokenSource(TimeSpan.FromSeconds(5));

            await _realtime.MusicStateChangedAsync(
                state,
                timeout.Token);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "AI playback publication failed for room {RoomId}.",
                state.RoomId);
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

    private static void RequireOwner(
        Room? room,
        Guid guestId)
    {
        if (room is null || room.Status != RoomStatus.Active ||
            room.GetRole(guestId) is null)
        {
            throw Error(
                ApplicationErrorCode.RoomNotFound,
                "Room was not found.");
        }

        if (room.GetRole(guestId) != RoomRole.Owner)
        {
            throw Error(
                ApplicationErrorCode.AccessDenied,
                "Only the room owner can generate AI playlists.");
        }
    }

    private static RoomApplicationException Error(
        ApplicationErrorCode code,
        string message)
    {
        return new RoomApplicationException(code, message);
    }
}