namespace VibyRoom.Application.Abstractions.Exceptions;

public sealed class RoomQueueConcurrencyException : Exception
{
    public RoomQueueConcurrencyException(
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
    }
}