namespace VibyRoom.Application.Errors;

public sealed class MusicCatalogApplicationException : Exception
{
    public MusicCatalogApplicationException(
        ApplicationErrorCode errorCode,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        if (errorCode is not (
            ApplicationErrorCode.Validation or
            ApplicationErrorCode.TrackNotFound or
            ApplicationErrorCode.UnsupportedMusicProvider or
            ApplicationErrorCode.MusicProviderUnavailable or
            ApplicationErrorCode.MusicProviderRateLimited))
        {
            throw new ArgumentOutOfRangeException(
                nameof(errorCode),
                errorCode,
                "Unsupported music catalog error code.");
        }

        ErrorCode = errorCode;
    }

    public ApplicationErrorCode ErrorCode { get; }
}