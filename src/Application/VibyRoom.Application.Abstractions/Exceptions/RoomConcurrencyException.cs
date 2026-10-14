namespace VibyRoom.Application.Abstractions.Exceptions;

public sealed class RoomConcurrencyException : Exception
{
    public RoomConcurrencyException(
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
    }
}