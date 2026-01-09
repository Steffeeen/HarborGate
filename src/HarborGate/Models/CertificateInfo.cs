using System.Security.Cryptography.X509Certificates;

namespace HarborGate.Models;

/// <summary>
/// Information about a stored certificate
/// </summary>
public class CertificateInfo
{
    /// <summary>
    /// The hostname this certificate is for
    /// </summary>
    public required string Hostname { get; set; }

    /// <summary>
    /// The certificate
    /// </summary>
    public required X509Certificate2 Certificate { get; set; }

    /// <summary>
    /// When the certificate was created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the certificate expires
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// The type of certificate (SelfSigned, LetsEncrypt, etc.)
    /// </summary>
    public string CertificateType { get; set; } = "Unknown";

    /// <summary>
    /// Checks if the certificate is expired
    /// </summary>
    public bool IsExpired => DateTime.UtcNow > ExpiresAt;

    /// <summary>
    /// Checks if the certificate expires within the specified timespan
    /// </summary>
    public bool ExpiresWithin(TimeSpan timespan) => 
        DateTime.UtcNow.Add(timespan) > ExpiresAt;
}
