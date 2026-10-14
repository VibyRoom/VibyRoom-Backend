namespace VibyRoom.Domain.Core.Entities.Rooms.Exceprions;

public sealed class RoomDomainException : Exception
{
    public RoomDomainErrorCode ErrorCode { get; }

    public RoomDomainException(
        RoomDomainErrorCode errorCode,
        string message)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        if (!Enum.IsDefined(errorCode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(errorCode),
                errorCode,
                "Unknown room domain error code.");
        }

        ErrorCode = errorCode;
    }
}