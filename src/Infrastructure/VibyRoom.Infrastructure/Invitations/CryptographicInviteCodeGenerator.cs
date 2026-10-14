using System.Security.Cryptography;
using VibyRoom.Application.Abstractions.Invitations;

namespace VibyRoom.Infrastructure.Invitations;

public sealed class CryptographicInviteCodeGenerator
    : IInviteCodeGenerator
{
    private const int RandomBytesCount = 16;

    public string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(
            RandomBytesCount);

        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}