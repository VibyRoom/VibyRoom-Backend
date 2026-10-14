using Microsoft.AspNetCore.DataProtection;

namespace VibyRoom.Api.Authentication;

public static class GuestDataProtectionExtensions
{
    private const string DefaultApplicationName = "VibyRoom";

    private const string ApplicationNameKey =
        "DataProtection:ApplicationName";

    private const string KeysDirectoryKey =
        "DataProtection:KeysDirectory";

    public static IServiceCollection AddGuestDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var applicationName = configuration[ApplicationNameKey];

        if (string.IsNullOrWhiteSpace(applicationName))
        {
            applicationName = DefaultApplicationName;
        }

        var dataProtection = services
            .AddDataProtection()
            .SetApplicationName(applicationName);

        var keysDirectory = configuration[KeysDirectoryKey];

        if (string.IsNullOrWhiteSpace(keysDirectory))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    $"Data Protection requires '{KeysDirectoryKey}' " +
                    "outside the Development environment.");
            }

            return services;
        }

        if (!Path.IsPathFullyQualified(keysDirectory))
        {
            throw new InvalidOperationException(
                $"'{KeysDirectoryKey}' must be an absolute path.");
        }

        var directory = new DirectoryInfo(keysDirectory);

        if (!directory.Exists)
        {
            throw new InvalidOperationException(
                $"Data Protection keys directory does not exist: " +
                $"'{directory.FullName}'.");
        }

        ValidateDirectoryAccess(directory);

        dataProtection.PersistKeysToFileSystem(directory);

        return services;
    }

    private static void ValidateDirectoryAccess(
        DirectoryInfo directory)
    {
        try
        {
            _ = directory.EnumerateFiles().Any();

            var probePath = Path.Combine(
                directory.FullName,
                $".write-check-{Guid.NewGuid():N}");

            using var stream = new FileStream(
                probePath,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    Options = FileOptions.DeleteOnClose
                });
        }
        catch (Exception exception)
            when (exception is IOException
                  or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "Data Protection keys directory is not accessible " +
                "for reading and writing.",
                exception);
        }
    }
}