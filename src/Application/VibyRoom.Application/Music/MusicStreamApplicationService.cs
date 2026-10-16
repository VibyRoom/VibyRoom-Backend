using VibyRoom.Application.Abstractions.Authentication;
using VibyRoom.Application.Abstractions.Exceptions;
using VibyRoom.Application.Abstractions.Music;
using VibyRoom.Application.Contracts.Music;
using VibyRoom.Application.Errors;
using VibyRoom.Application.Playback;

namespace VibyRoom.Application.Music;

public sealed class MusicStreamApplicationService
{
    private readonly ICurrentGuest _currentGuest;
    private readonly RoomPlaybackApplicationService _playback;
    private readonly IMusicStreamProvider _streamProvider;

    public MusicStreamApplicationService(
        ICurrentGuest currentGuest,
        RoomPlaybackApplicationService playback,
        IMusicStreamProvider streamProvider)
    {
        _currentGuest = currentGuest
            ?? throw new ArgumentNullException(nameof(currentGuest));

        _playback = playback
            ?? throw new ArgumentNullException(nameof(playback));

        _streamProvider = streamProvider
            ?? throw new ArgumentNullException(nameof(streamProvider));
    }

    public async Task<PlaybackSource> GetCurrentStreamAsync(
        Guid roomId,
        Guid expectedItemId,
        CancellationToken cancellationToken = default)
    {
        if (_currentGuest.GuestId is not { } guestId ||
            guestId == Guid.Empty)
        {
            throw Error(
                ApplicationErrorCode.GuestSessionRequired,
                "An active guest session is required.");
        }

        if (roomId == Guid.Empty || expectedItemId == Guid.Empty)
        {
            throw Error(
                ApplicationErrorCode.Validation,
                "Valid room ID and playback item ID are required.");
        }

        // Проверка активной комнаты и членства уже реализована
        // в RoomPlaybackApplicationService.
        // Там же актуализируется просроченное воспроизведение.
        var state = await _playback.GetMusicStateAsync(
            roomId,
            cancellationToken);

        var currentItem = state.Playback.CurrentItem;

        if (currentItem is null)
        {
            throw Error(
                ApplicationErrorCode.PlaybackNotActive,
                "The room has no active playback item.");
        }

        if (currentItem.Id != expectedItemId)
        {
            throw Error(
                ApplicationErrorCode.PlaybackItemMismatch,
                "The requested item is not the current playback item.");
        }

        PlaybackSource? source;

        try
        {
            source = await _streamProvider.GetStreamSourceAsync(
                currentItem.Track.Reference,
                cancellationToken);
        }
        catch (MusicProviderException exception)
        {
            throw MapProviderError(exception);
        }

        if (source is null)
        {
            throw Error(
                ApplicationErrorCode.TrackNotStreamable,
                "The current track is not publicly streamable.");
        }

        if (source.Reference != currentItem.Track.Reference)
        {
            throw Error(
                ApplicationErrorCode.MusicProviderUnavailable,
                "The provider returned an inconsistent track source.");
        }

        // Повторно проверяем членство и актуальный ItemId
        // после внешнего HTTP-запроса к Audius.
        // Трек мог измениться, комната — закрыться,
        // а участник — потерять доступ.
        var latestState = await _playback.GetMusicStateAsync(
            roomId,
            cancellationToken);

        if (latestState.Playback.CurrentItem?.Id != expectedItemId)
        {
            throw Error(
                ApplicationErrorCode.PlaybackItemMismatch,
                "Playback changed while resolving the audio source.");
        }

        return source;
    }

    private static RoomApplicationException MapProviderError(
        MusicProviderException exception)
    {
        var code = exception.FailureKind switch
        {
            MusicProviderFailureKind.AccessDenied =>
                ApplicationErrorCode.StreamAccessDenied,

            MusicProviderFailureKind.RateLimited =>
                ApplicationErrorCode.MusicProviderRateLimited,

            _ => ApplicationErrorCode.MusicProviderUnavailable
        };

        return new RoomApplicationException(
            code,
            code switch
            {
                ApplicationErrorCode.StreamAccessDenied =>
                    "Audius denied access to the audio stream.",

                ApplicationErrorCode.MusicProviderRateLimited =>
                    "Audius rate limit exceeded.",

                _ => "Audius streaming service is unavailable."
            },
            exception);
    }

    private static RoomApplicationException Error(
        ApplicationErrorCode code,
        string message)
    {
        return new RoomApplicationException(code, message);
    }
}