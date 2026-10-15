using VibyRoom.Api.Contracts.Music;
using VibyRoom.Application.Contracts.Music;

namespace VibyRoom.Api.Mapping;

public static class MusicTrackResponseMapper
{
    public static MusicTrackResponse ToResponse(
        MusicTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);

        var reference = new TrackReferenceResponse(
            track.Reference.Provider,
            track.Reference.TrackId);

        return new MusicTrackResponse(
            reference,
            track.Title,
            track.Artists.ToArray(),
            track.Album,
            track.DurationMs,
            track.ArtworkUrl,
            track.ExternalUrl);
    }

    public static TrackSearchResponse ToSearchResponse(
        IReadOnlyList<MusicTrack> tracks)
    {
        ArgumentNullException.ThrowIfNull(tracks);

        var responses = tracks
            .Select(ToResponse)
            .ToArray();

        return new TrackSearchResponse(responses);
    }
}