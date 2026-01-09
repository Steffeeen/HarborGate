using HarborGate.Certificates;

namespace HarborGate.Services;

/// <summary>
/// Background service that periodically checks and renews certificates
/// </summary>
public class CertificateRenewalService : BackgroundService
{
    private readonly ICertificateProvider _certificateProvider;
    private readonly ILogger<CertificateRenewalService> _logger;
    private readonly TimeSpan _checkInterval;

    public CertificateRenewalService(
        ICertificateProvider certificateProvider,
        ILogger<CertificateRenewalService> logger)
    {
        _certificateProvider = certificateProvider;
        _logger = logger;
        _checkInterval = TimeSpan.FromHours(12); // Check twice daily
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Certificate renewal service starting");

        // Wait a bit before first check to let the application start up
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndRenewCertificatesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during certificate renewal check");
            }

            // Wait until next check
            await Task.Delay(_checkInterval, stoppingToken);
        }

        _logger.LogInformation("Certificate renewal service stopping");
    }

    private async Task CheckAndRenewCertificatesAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Checking certificates for renewal");

        var hostnames = await _certificateProvider.GetAllHostnamesAsync(cancellationToken);
        
        if (hostnames.Count == 0)
        {
            _logger.LogDebug("No certificates to check");
            return;
        }

        _logger.LogInformation("Checking {Count} certificates", hostnames.Count);

        foreach (var hostname in hostnames)
        {
            try
            {
                var needsRenewal = await _certificateProvider.NeedsRenewalAsync(hostname, cancellationToken);
                
                if (needsRenewal)
                {
                    _logger.LogInformation("Certificate for {Hostname} needs renewal, requesting new certificate", hostname);
                    
                    var newCert = await _certificateProvider.RenewCertificateAsync(hostname, cancellationToken);
                    
                    if (newCert != null)
                    {
                        _logger.LogInformation(
                            "Successfully renewed certificate for {Hostname}, expires: {ExpiresAt}",
                            hostname, newCert.NotAfter);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to renew certificate for {Hostname}", hostname);
                    }
                }
                else
                {
                    _logger.LogDebug("Certificate for {Hostname} does not need renewal", hostname);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking/renewing certificate for {Hostname}", hostname);
            }
        }

        _logger.LogInformation("Certificate renewal check complete");
    }
}
