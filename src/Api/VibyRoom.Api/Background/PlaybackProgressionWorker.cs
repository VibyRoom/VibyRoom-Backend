using Microsoft.Extensions.Options;
using VibyRoom.Application.Abstractions.Repositories;
using VibyRoom.Application.Playback;

namespace VibyRoom.Api.Background;

public sealed class PlaybackProgressionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly PlaybackProgressionOptions _options;
    private readonly ILogger<PlaybackProgressionWorker> _logger;

    public PlaybackProgressionWorker(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<PlaybackProgressionOptions> options,
        ILogger<PlaybackProgressionWorker> logger)
    {
        _scopeFactory = scopeFactory
            ?? throw new ArgumentNullException(nameof(scopeFactory));

        _timeProvider = timeProvider
            ?? throw new ArgumentNullException(nameof(timeProvider));

        _options = options?.Value
            ?? throw new ArgumentNullException(nameof(options));

        _logger = logger
            ?? throw new ArgumentNullException(nameof(logger));

        if (_options.IntervalMilliseconds < 100 ||
            _options.IntervalMilliseconds > 60000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Playback polling interval must be between 100 and 60000 ms.");
        }

        if (_options.BatchSize is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Playback batch size must be between 1 and 500.");
        }
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Playback progression worker started. " +
            "Interval: {IntervalMs} ms. Batch size: {BatchSize}.",
            _options.IntervalMilliseconds,
            _options.BatchSize);

        // Первый проход сразу после запуска Backend.
        // Это необходимо для восстановления просроченного
        // воспроизведения после перезапуска сервера.
        await ProcessBatchSafelyAsync(stoppingToken);

        using var timer = new PeriodicTimer(
            TimeSpan.FromMilliseconds(
                _options.IntervalMilliseconds),
            _timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await ProcessBatchSafelyAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            // Штатная остановка приложения.
        }

        _logger.LogInformation(
            "Playback progression worker stopped.");
    }

    private async Task ProcessBatchSafelyAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            await ProcessBatchAsync(cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            // Не скрываем отмену выполнения.
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Playback progression batch failed. " +
                "The next polling cycle will retry.");
        }
    }

    private async Task ProcessBatchAsync(
        CancellationToken cancellationToken)
    {
        List<Guid> dueRoomIds;

        // Краткоживущий scope только для выборки.
        // Не удерживаем DbContext между итерациями.
        await using (var scope =
                     _scopeFactory.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider
                .GetRequiredService<IRoomMusicSessionRepository>();

            dueRoomIds = await repository.GetDueRoomIdsAsync(
                _timeProvider.GetUtcNow(),
                _options.BatchSize,
                cancellationToken);
        }

        if (dueRoomIds.Count == 0)
        {
            return;
        }

        _logger.LogDebug(
            "Found {Count} overdue playback sessions.",
            dueRoomIds.Count);

        foreach (var roomId in dueRoomIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await ProcessRoomSafelyAsync(
                roomId,
                cancellationToken);
        }
    }

    private async Task ProcessRoomSafelyAsync(
        Guid roomId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Каждая комната обрабатывается независимо,
            // в собственном scope и транзакции.
            await using var scope =
                _scopeFactory.CreateAsyncScope();

            var playback = scope.ServiceProvider
                .GetRequiredService<RoomPlaybackApplicationService>();

            var changed = await playback.AdvanceRoomIfDueAsync(
                roomId,
                cancellationToken);

            if (changed)
            {
                _logger.LogDebug(
                    "Playback advanced for room {RoomId}.",
                    roomId);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Сбой одной комнаты не останавливает остальные.
            _logger.LogError(
                exception,
                "Failed to advance playback for room {RoomId}.",
                roomId);
        }
    }
}