using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace VibyRoom.Infrastructure.Ai;

internal static class GigaChatHttpHandlerFactory
{
    public static HttpMessageHandler Create(GigaChatOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false
        };

        if (string.IsNullOrWhiteSpace(
                options.TrustedRootCertificatePath))
        {
            return handler;
        }

        var certificate =
            X509CertificateLoader.LoadCertificateFromFile(
                options.TrustedRootCertificatePath);

        var chainPolicy = new X509ChainPolicy
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck
        };

        chainPolicy.CustomTrustStore.Add(certificate);

        handler.SslOptions = new SslClientAuthenticationOptions
        {
            CertificateChainPolicy = chainPolicy
        };

        return handler;
    }
}