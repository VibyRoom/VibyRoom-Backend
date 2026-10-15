namespace VibyRoom.Application.Abstractions.Exceptions;

public sealed class MusicProviderException : Exception
{
    public MusicProviderException(
        MusicProviderFailureKind failureKind,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        if (!Enum.IsDefined(failureKind))
        {
            throw new ArgumentOutOfRangeException(
                nameof(failureKind),
                failureKind,
                "Unknown music provider failure kind.");
        }

        FailureKind = failureKind;
    }

    public MusicProviderFailureKind FailureKind { get; }
}