namespace VibyRoom.Domain.Music.Queue.Exceptions;

public sealed class RoomQueueDomainException : Exception
{
    public RoomQueueDomainException(
        RoomQueueDomainErrorCode errorCode,
        string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        if (!Enum.IsDefined(errorCode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(errorCode),
                errorCode,
                "Unknown room queue domain error code.");
        }

        ErrorCode = errorCode;
    }

    public RoomQueueDomainErrorCode ErrorCode { get; }
}