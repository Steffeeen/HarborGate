using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using HarborGate.Models;

namespace HarborGate.Services;

/// <summary>
/// Manages certificate storage and retrieval
/// Stores certificates in memory and optionally persists to disk
/// </summary>
public class CertificateStorageService
{
    private readonly ConcurrentDictionary<string, CertificateInfo> _certificates = new();
    private readonly ILogger<CertificateStorageService> _logger;
    private readonly string _storagePath;

    public CertificateStorageService(ILogger<CertificateStorageService> logger, string storagePath)
    {
        _logger = logger;
        _storagePath = storagePath;

        // Ensure storage directory exists
        if (!string.IsNullOrEmpty(_storagePath))
        {
            Directory.CreateDirectory(_storagePath);
            _logger.LogInformation("Certificate storage initialized at: {StoragePath}", _storagePath);
        }
        else
        {
            _logger.LogWarning("Certificate storage path not configured - certificates will only be stored in memory");
        }
    }

    /// <summary>
    /// Stores a certificate for a hostname
    /// </summary>
    public async Task StoreCertificateAsync(string hostname, X509Certificate2 certificate, string certificateType)
    {
        var certInfo = new CertificateInfo
        {
            Hostname = hostname,
            Certificate = certificate,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = certificate.NotAfter.ToUniversalTime(),
            CertificateType = certificateType
        };

        _certificates.AddOrUpdate(hostname, certInfo, (_, _) => certInfo);
        _logger.LogInformation(
            "Certificate stored for {Hostname}, Type: {Type}, Expires: {ExpiresAt}",
            hostname, certificateType, certInfo.ExpiresAt);

        // Persist to disk if storage path is configured
        if (!string.IsNullOrEmpty(_storagePath))
        {
            await PersistCertificateToDiskAsync(hostname, certificate);
        }
    }

    /// <summary>
    /// Retrieves a certificate for a hostname
    /// </summary>
    public X509Certificate2? GetCertificate(string hostname)
    {
        if (_certificates.TryGetValue(hostname, out var certInfo))
        {
            if (!certInfo.IsExpired)
            {
                return certInfo.Certificate;
            }

            _logger.LogWarning("Certificate for {Hostname} is expired", hostname);
        }

        return null;
    }

    /// <summary>
    /// Retrieves certificate info for a hostname
    /// </summary>
    public CertificateInfo? GetCertificateInfo(string hostname)
    {
        return _certificates.TryGetValue(hostname, out var certInfo) ? certInfo : null;
    }

    /// <summary>
    /// Gets all stored certificates
    /// </summary>
    public IReadOnlyList<CertificateInfo> GetAllCertificates()
    {
        return _certificates.Values.ToList();
    }

    /// <summary>
    /// Gets all hostnames with certificates
    /// </summary>
    public IReadOnlyList<string> GetAllHostnames()
    {
        return _certificates.Keys.ToList();
    }

    /// <summary>
    /// Removes a certificate for a hostname
    /// </summary>
    public void RemoveCertificate(string hostname)
    {
        if (_certificates.TryRemove(hostname, out var certInfo))
        {
            _logger.LogInformation("Certificate removed for {Hostname}", hostname);
            certInfo.Certificate.Dispose();

            // Remove from disk if storage path is configured
            if (!string.IsNullOrEmpty(_storagePath))
            {
                DeleteCertificateFromDisk(hostname);
            }
        }
    }

    /// <summary>
    /// Loads certificates from disk storage
    /// </summary>
    public async Task LoadCertificatesFromDiskAsync()
    {
        if (string.IsNullOrEmpty(_storagePath) || !Directory.Exists(_storagePath))
        {
            return;
        }

        try
        {
            var pfxFiles = Directory.GetFiles(_storagePath, "*.pfx");
            _logger.LogInformation("Loading {Count} certificates from disk", pfxFiles.Length);

            foreach (var pfxFile in pfxFiles)
            {
                try
                {
                    var hostname = Path.GetFileNameWithoutExtension(pfxFile);
                    var pfxBytes = await File.ReadAllBytesAsync(pfxFile);
                    var certificate = X509CertificateLoader.LoadPkcs12(pfxBytes, null, X509KeyStorageFlags.Exportable);

                    var certInfo = new CertificateInfo
                    {
                        Hostname = hostname,
                        Certificate = certificate,
                        CreatedAt = File.GetCreationTimeUtc(pfxFile),
                        ExpiresAt = certificate.NotAfter.ToUniversalTime(),
                        CertificateType = "Loaded" // We don't store type in file name
                    };

                    _certificates.TryAdd(hostname, certInfo);
                    _logger.LogInformation(
                        "Loaded certificate for {Hostname}, Expires: {ExpiresAt}",
                        hostname, certInfo.ExpiresAt);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load certificate from {PfxFile}", pfxFile);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load certificates from disk");
        }
    }

    /// <summary>
    /// Persists a certificate to disk
    /// </summary>
    private async Task PersistCertificateToDiskAsync(string hostname, X509Certificate2 certificate)
    {
        try
        {
            var sanitizedHostname = SanitizeFilename(hostname);
            var pfxPath = Path.Combine(_storagePath, $"{sanitizedHostname}.pfx");
            var pfxBytes = certificate.Export(X509ContentType.Pfx);
            await File.WriteAllBytesAsync(pfxPath, pfxBytes);
            _logger.LogDebug("Certificate persisted to disk: {PfxPath}", pfxPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist certificate for {Hostname} to disk", hostname);
        }
    }

    /// <summary>
    /// Deletes a certificate from disk
    /// </summary>
    private void DeleteCertificateFromDisk(string hostname)
    {
        try
        {
            var sanitizedHostname = SanitizeFilename(hostname);
            var pfxPath = Path.Combine(_storagePath, $"{sanitizedHostname}.pfx");
            if (File.Exists(pfxPath))
            {
                File.Delete(pfxPath);
                _logger.LogDebug("Certificate deleted from disk: {PfxPath}", pfxPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete certificate for {Hostname} from disk", hostname);
        }
    }

    /// <summary>
    /// Sanitizes a hostname for use as a filename
    /// </summary>
    private static string SanitizeFilename(string filename)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", filename.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
    }
}
