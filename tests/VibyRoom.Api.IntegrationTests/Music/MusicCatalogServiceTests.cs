using VibyRoom.Application.Abstractions.Exceptions;
using VibyRoom.Application.Contracts.Music;
using VibyRoom.Application.Errors;
using VibyRoom.Application.Music;
using VibyRoom.Domain.Music.Tracks;
using Xunit;

namespace VibyRoom.Api.IntegrationTests.Music;

public sealed class MusicCatalogServiceTests
{
    private readonly FakeMusicCatalogProvider _provider = new();

    private MusicCatalogService CreateService() =>
        new(_provider);

    [Fact]
    public async Task Search_ValidQuery_TrimsAndPassesLimit()
    {
        var track = CreateTrack();

        _provider.SearchBehavior = (_, _, _) =>
            Task.FromResult<IReadOnlyList<MusicTrack>>(
                new[] { track });

        var result = await CreateService().SearchTracksAsync(
            "  electronic music  ",
            15,
            TestContext.Current.CancellationToken);

        Assert.Equal("electronic music", _provider.LastSearchQuery);
        Assert.Equal(15, _provider.LastSearchLimit);
        Assert.Equal(1, _provider.SearchCalls);
        Assert.Same(track, Assert.Single(result));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")]
    [InlineData(" a ")]
    public async Task Search_InvalidQuery_ReturnsValidation(
        string? query)
    {
        var exception =
            await Assert.ThrowsAsync<MusicCatalogApplicationException>(
                () => CreateService().SearchTracksAsync(
                    query!,
                    20,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            ApplicationErrorCode.Validation,
            exception.ErrorCode);

        Assert.Equal(0, _provider.SearchCalls);
    }

    [Fact]
    public async Task Search_TooLongQuery_ReturnsValidation()
    {
        var exception =
            await Assert.ThrowsAsync<MusicCatalogApplicationException>(
                () => CreateService().SearchTracksAsync(
                    new string('a', 101),
                    20,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            ApplicationErrorCode.Validation,
            exception.ErrorCode);

        Assert.Equal(0, _provider.SearchCalls);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(51)]
    public async Task Search_InvalidLimit_ReturnsValidation(int limit)
    {
        var exception =
            await Assert.ThrowsAsync<MusicCatalogApplicationException>(
                () => CreateService().SearchTracksAsync(
                    "electronic",
                    limit,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            ApplicationErrorCode.Validation,
            exception.ErrorCode);

        Assert.Equal(0, _provider.SearchCalls);
    }

    [Fact]
    public async Task Search_NoMatches_ReturnsEmptyCollection()
    {
        var result = await CreateService().SearchTracksAsync(
            "unknown music",
            20,
            TestContext.Current.CancellationToken);

        Assert.Empty(result);
        Assert.Equal(1, _provider.SearchCalls);
    }

    [Fact]
    public async Task Search_RateLimited_MapsToApplicationError()
    {
        _provider.SearchBehavior = (_, _, _) =>
            Task.FromException<IReadOnlyList<MusicTrack>>(
                new MusicProviderException(
                    MusicProviderFailureKind.RateLimited,
                    "Rate limit exceeded."));

        var exception =
            await Assert.ThrowsAsync<MusicCatalogApplicationException>(
                () => CreateService().SearchTracksAsync(
                    "electronic",
                    20,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            ApplicationErrorCode.MusicProviderRateLimited,
            exception.ErrorCode);
    }

    [Fact]
    public async Task Search_InvalidProviderTrack_IsRejected()
    {
        var track = CreateTrack("another-provider");

        _provider.SearchBehavior = (_, _, _) =>
            Task.FromResult<IReadOnlyList<MusicTrack>>(
                new[] { track });

        var exception =
            await Assert.ThrowsAsync<MusicCatalogApplicationException>(
                () => CreateService().SearchTracksAsync(
                    "electronic",
                    20,
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            ApplicationErrorCode.MusicProviderUnavailable,
            exception.ErrorCode);
    }

    [Fact]
    public async Task Search_PassesCancellationToken()
    {
        using var source = new CancellationTokenSource();

        await CreateService().SearchTracksAsync(
            "electronic",
            20,
            source.Token);

        Assert.Equal(
            source.Token,
            _provider.LastCancellationToken);
    }

    [Fact]
    public async Task Details_ValidReference_ReturnsTrack()
    {
        var track = CreateTrack();

        _provider.DetailsBehavior = (_, _) =>
            Task.FromResult<MusicTrack?>(track);

        var result = await CreateService().GetTrackAsync(
            track.Reference,
            TestContext.Current.CancellationToken);

        Assert.Same(track, result);
        Assert.Equal("D7KyD", _provider.LastTrackId);
        Assert.Equal(1, _provider.DetailsCalls);
    }

    [Fact]
    public async Task Details_UnknownTrack_ReturnsNull()
    {
        var result = await CreateService().GetTrackAsync(
            new TrackReference("audius", "D7KyD"),
            TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Equal(1, _provider.DetailsCalls);
    }

    [Fact]
    public async Task Details_UnsupportedProvider_IsRejected()
    {
        var exception =
            await Assert.ThrowsAsync<MusicCatalogApplicationException>(
                () => CreateService().GetTrackAsync(
                    new TrackReference("spotify", "D7KyD"),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            ApplicationErrorCode.UnsupportedMusicProvider,
            exception.ErrorCode);

        Assert.Equal(0, _provider.DetailsCalls);
    }

    [Fact]
    public async Task Details_MismatchedTrackId_IsRejected()
    {
        _provider.DetailsBehavior = (_, _) =>
            Task.FromResult<MusicTrack?>(
                CreateTrack(trackId: "AnotherId"));

        var exception =
            await Assert.ThrowsAsync<MusicCatalogApplicationException>(
                () => CreateService().GetTrackAsync(
                    new TrackReference("audius", "D7KyD"),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            ApplicationErrorCode.MusicProviderUnavailable,
            exception.ErrorCode);
    }

    [Fact]
    public async Task Details_ProviderFailure_IsNotConvertedToNull()
    {
        _provider.DetailsBehavior = (_, _) =>
            Task.FromException<MusicTrack?>(
                new MusicProviderException(
                    MusicProviderFailureKind.Unavailable,
                    "Network failure."));

        var exception =
            await Assert.ThrowsAsync<MusicCatalogApplicationException>(
                () => CreateService().GetTrackAsync(
                    new TrackReference("audius", "D7KyD"),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            ApplicationErrorCode.MusicProviderUnavailable,
            exception.ErrorCode);
    }

    [Fact]
    public async Task Details_PassesCancellationToken()
    {
        using var source = new CancellationTokenSource();

        await CreateService().GetTrackAsync(
            new TrackReference("audius", "D7KyD"),
            source.Token);

        Assert.Equal(
            source.Token,
            _provider.LastCancellationToken);
    }

    private static MusicTrack CreateTrack(
        string provider = "audius",
        string trackId = "D7KyD")
    {
        return new MusicTrack(
            new TrackReference(provider, trackId),
            "Test Track",
            new[] { "Test Artist" },
            album: null,
            durationMs: 180000,
            artworkUrl: null,
            externalUrl: null);
    }
}