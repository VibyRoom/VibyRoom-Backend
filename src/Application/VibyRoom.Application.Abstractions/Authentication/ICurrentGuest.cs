namespace VibyRoom.Application.Abstractions.Authentication;

public interface ICurrentGuest
{
    Guid? GuestId { get; }
}