using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using HarborGate.Certificates;
using Microsoft.AspNetCore.Connections;

namespace HarborGate.Services;

/// <summary>
/// Service that selects certificates dynamically based on SNI (Server Name Indication)
/// </summary>
public class DynamicCertificateSelector
{
    private readonly ICertificateProvider _certificateProvider;
    private readonly ILogger<DynamicCertificateSelector> _logger;

    public DynamicCertificateSelector(
        ICertificateProvider certificateProvider,
        ILogger<DynamicCertificateSelector> logger)
    {
        _certificateProvider = certificateProvider;
        _logger = logger;
    }

    /// <summary>
    /// Callback for Kestrel to select certificate based on SNI hostname
    /// </summary>
    public async ValueTask<X509Certificate2?> SelectCertificateAsync(
        ConnectionContext? context, 
        string? hostname)
    {
        if (string.IsNullOrEmpty(hostname))
        {
            _logger.LogWarning("No hostname provided in SNI, cannot select certificate");
            return null;
        }

        _logger.LogDebug("Selecting certificate for hostname: {Hostname}", hostname);

        try
        {
            var cancellationToken = context?.ConnectionClosed ?? CancellationToken.None;
            var certificate = await _certificateProvider.GetCertificateAsync(hostname, cancellationToken);
            
            if (certificate == null)
            {
                _logger.LogWarning("No certificate available for hostname: {Hostname}", hostname);
            }
            else
            {
                _logger.LogDebug(
                    "Selected certificate for {Hostname}, expires: {ExpiresAt}",
                    hostname, certificate.NotAfter);
            }

            return certificate;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get certificate for hostname: {Hostname}", hostname);
            return null;
        }
    }
}
