namespace VibyRoom.Application.Abstractions.Exceptions;

public enum MusicProviderFailureKind
{
    Unavailable,
    RateLimited,
    AccessDenied,
    InvalidResponse
}