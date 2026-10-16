namespace VibyRoom.Domain.Music.Queue.Exceptions;

public enum RoomQueueDomainErrorCode
{
    QueueFull,
    QueueItemNotFound,
    AccessDenied,
    InvalidQueuePosition
}