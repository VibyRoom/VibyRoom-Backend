namespace VibyRoom.Application.Abstractions.Exceptions;

public sealed class RoomInviteCodeConflictException : Exception
{
    public RoomInviteCodeConflictException(
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
    }
}