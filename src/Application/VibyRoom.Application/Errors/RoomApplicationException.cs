namespace VibyRoom.Application.Errors;

public sealed class RoomApplicationException : Exception
{
    public ApplicationErrorCode ErrorCode { get; }

    public RoomApplicationException(
        ApplicationErrorCode errorCode,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        if (!Enum.IsDefined(errorCode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(errorCode),
                errorCode,
                "Unknown application error code.");
        }

        ErrorCode = errorCode;
    }
}