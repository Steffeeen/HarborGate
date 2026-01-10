using System.Security.Cryptography.X509Certificates;

namespace HarborGate.Certificates;

/// <summary>
/// Interface for certificate providers (self-signed, Let's Encrypt, etc.)
/// </summary>
public interface ICertificateProvider
{
    /// <summary>
    /// Gets or creates a certificate for the specified hostname
    /// </summary>
    /// <param name="hostname">The hostname to get a certificate for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The X509Certificate2 for the hostname</returns>
    Task<X509Certificate2?> GetCertificateAsync(string hostname, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a certificate needs renewal
    /// </summary>
    /// <param name="hostname">The hostname to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the certificate needs renewal</returns>
    Task<bool> NeedsRenewalAsync(string hostname, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renews a certificate for the specified hostname
    /// </summary>
    /// <param name="hostname">The hostname to renew the certificate for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The renewed X509Certificate2</returns>
    Task<X509Certificate2?> RenewCertificateAsync(string hostname, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all hostnames that have certificates
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of hostnames with certificates</returns>
    Task<IReadOnlyList<string>> GetAllHostnamesAsync(CancellationToken cancellationToken = default);
}
