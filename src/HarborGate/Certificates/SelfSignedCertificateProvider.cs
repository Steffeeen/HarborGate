using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using HarborGate.Services;

namespace HarborGate.Certificates;

/// <summary>
/// Certificate provider that creates self-signed certificates for local development
/// </summary>
public class SelfSignedCertificateProvider : ICertificateProvider
{
    private readonly CertificateStorageService _storage;
    private readonly ILogger<SelfSignedCertificateProvider> _logger;
    private readonly TimeSpan _certificateValidity;

    public SelfSignedCertificateProvider(
        CertificateStorageService storage,
        ILogger<SelfSignedCertificateProvider> logger,
        TimeSpan? certificateValidity = null)
    {
        _storage = storage;
        _logger = logger;
        _certificateValidity = certificateValidity ?? TimeSpan.FromDays(365); // Default: 1 year
    }

    public async Task<X509Certificate2?> GetCertificateAsync(string hostname, CancellationToken cancellationToken = default)
    {
        // Check if we already have a valid certificate
        var existingCert = _storage.GetCertificate(hostname);
        if (existingCert != null)
        {
            _logger.LogDebug("Using cached self-signed certificate for {Hostname}", hostname);
            return existingCert;
        }

        // Create a new self-signed certificate
        _logger.LogInformation("Creating new self-signed certificate for {Hostname}", hostname);
        var certificate = CreateSelfSignedCertificate(hostname);

        if (certificate != null)
        {
            await _storage.StoreCertificateAsync(hostname, certificate, "SelfSigned");
        }

        return certificate;
    }

    public Task<bool> NeedsRenewalAsync(string hostname, CancellationToken cancellationToken = default)
    {
        var certInfo = _storage.GetCertificateInfo(hostname);
        if (certInfo == null)
        {
            return Task.FromResult(true);
        }

        // Renew if certificate expires within 30 days
        var needsRenewal = certInfo.ExpiresWithin(TimeSpan.FromDays(30));
        
        if (needsRenewal)
        {
            _logger.LogInformation(
                "Self-signed certificate for {Hostname} needs renewal (expires {ExpiresAt})",
                hostname, certInfo.ExpiresAt);
        }

        return Task.FromResult(needsRenewal);
    }

    public async Task<X509Certificate2?> RenewCertificateAsync(string hostname, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Renewing self-signed certificate for {Hostname}", hostname);
        
        // Remove old certificate
        _storage.RemoveCertificate(hostname);

        // Create new certificate
        return await GetCertificateAsync(hostname, cancellationToken);
    }

    public Task<IReadOnlyList<string>> GetAllHostnamesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_storage.GetAllHostnames());
    }

    /// <summary>
    /// Creates a self-signed X509 certificate for the specified hostname
    /// </summary>
    private X509Certificate2? CreateSelfSignedCertificate(string hostname)
    {
        try
        {
            // Create a new RSA key pair
            using var rsa = RSA.Create(2048);

            // Create certificate request
            var request = new CertificateRequest(
                $"CN={hostname}",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            // Add Subject Alternative Name (SAN) extension
            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddDnsName(hostname);
            request.CertificateExtensions.Add(sanBuilder.Build());

            // Add basic constraints (not a CA)
            request.CertificateExtensions.Add(
                new X509BasicConstraintsExtension(false, false, 0, false));

            // Add key usage
            request.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                    false));

            // Add enhanced key usage for server authentication
            request.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(
                    new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, // Server Authentication
                    false));

            // Create self-signed certificate
            var notBefore = DateTimeOffset.UtcNow;
            var notAfter = notBefore.Add(_certificateValidity);
            
            var certificate = request.CreateSelfSigned(notBefore, notAfter);

            // Export and re-import to ensure the private key is included
            var pfxBytes = certificate.Export(X509ContentType.Pfx);
            var certWithKey = X509CertificateLoader.LoadPkcs12(pfxBytes, null, X509KeyStorageFlags.Exportable);

            _logger.LogInformation(
                "Created self-signed certificate for {Hostname}, valid from {NotBefore} to {NotAfter}",
                hostname, notBefore, notAfter);

            return certWithKey;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create self-signed certificate for {Hostname}", hostname);
            return null;
        }
    }
}
