using System.Security.Cryptography.X509Certificates;
using System.Net;
using System.Net.Security;
using Certes;
using Certes.Acme;
using Certes.Acme.Resource;
using HarborGate.Configuration;
using HarborGate.Services;
using HttpClient = System.Net.Http.HttpClient;
using HttpClientHandler = System.Net.Http.HttpClientHandler;

namespace HarborGate.Certificates;

/// <summary>
/// Certificate provider that uses Let's Encrypt via the ACME protocol
/// </summary>
public class LetsEncryptCertificateProvider : ICertificateProvider
{
    private readonly CertificateStorageService _storage;
    private readonly ILogger<LetsEncryptCertificateProvider> _logger;
    private readonly LetsEncryptOptions _options;
    private readonly IHttpChallengeStore _challengeStore;
    private AcmeContext? _acmeContext;
    private readonly SemaphoreSlim _acmeContextLock = new(1, 1);

    public LetsEncryptCertificateProvider(
        CertificateStorageService storage,
        ILogger<LetsEncryptCertificateProvider> logger,
        LetsEncryptOptions options,
        IHttpChallengeStore challengeStore)
    {
        _storage = storage;
        _logger = logger;
        _options = options;
        _challengeStore = challengeStore;

        if (string.IsNullOrEmpty(_options.Email))
        {
            throw new InvalidOperationException("Let's Encrypt email is required");
        }

        if (!_options.AcceptTermsOfService)
        {
            throw new InvalidOperationException(
                "You must accept the Let's Encrypt Terms of Service by setting AcceptTermsOfService=true");
        }
    }

    public async Task<X509Certificate2?> GetCertificateAsync(string hostname, CancellationToken cancellationToken = default)
    {
        // Check if we already have a valid certificate
        var existingCert = _storage.GetCertificate(hostname);
        if (existingCert != null)
        {
            _logger.LogDebug("Using cached Let's Encrypt certificate for {Hostname}", hostname);
            return existingCert;
        }

        // Request a new certificate from Let's Encrypt
        _logger.LogInformation("Requesting new Let's Encrypt certificate for {Hostname}", hostname);
        
        try
        {
            var certificate = await RequestCertificateAsync(hostname, cancellationToken);
            
            if (certificate != null)
            {
                await _storage.StoreCertificateAsync(hostname, certificate, "LetsEncrypt");
            }

            return certificate;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to request Let's Encrypt certificate for {Hostname}", hostname);
            return null;
        }
    }

    public Task<bool> NeedsRenewalAsync(string hostname, CancellationToken cancellationToken = default)
    {
        var certInfo = _storage.GetCertificateInfo(hostname);
        if (certInfo == null)
        {
            return Task.FromResult(true);
        }

        // Renew Let's Encrypt certificates 30 days before expiration (industry standard)
        var needsRenewal = certInfo.ExpiresWithin(TimeSpan.FromDays(30));
        
        if (needsRenewal)
        {
            _logger.LogInformation(
                "Let's Encrypt certificate for {Hostname} needs renewal (expires {ExpiresAt})",
                hostname, certInfo.ExpiresAt);
        }

        return Task.FromResult(needsRenewal);
    }

    public async Task<X509Certificate2?> RenewCertificateAsync(string hostname, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Renewing Let's Encrypt certificate for {Hostname}", hostname);
        
        // Remove old certificate
        _storage.RemoveCertificate(hostname);

        // Request new certificate
        return await GetCertificateAsync(hostname, cancellationToken);
    }

    public Task<IReadOnlyList<string>> GetAllHostnamesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_storage.GetAllHostnames());
    }

    /// <summary>
    /// Requests a new certificate from Let's Encrypt
    /// </summary>
    private async Task<X509Certificate2?> RequestCertificateAsync(string hostname, CancellationToken cancellationToken)
    {
        try
        {
            // Get or create ACME context
            var acme = await GetAcmeContextAsync(cancellationToken);

            // Create new order for the domain
            _logger.LogInformation("Creating ACME order for {Hostname}", hostname);
            var order = await acme.NewOrder(new[] { hostname });

            // Get authorization and HTTP-01 challenge
            var authz = (await order.Authorizations()).First();
            var httpChallenge = await authz.Http();
            
            if (httpChallenge == null)
            {
                _logger.LogError("No HTTP-01 challenge available for {Hostname}", hostname);
                return null;
            }

            // Store the challenge token for HTTP-01 validation
            var token = httpChallenge.Token;
            var keyAuthz = httpChallenge.KeyAuthz;
            
            _logger.LogInformation(
                "Storing HTTP-01 challenge for {Hostname}: token={Token}",
                hostname, token);
            
            _challengeStore.AddChallenge(token, keyAuthz);

            try
            {
                // Validate the challenge
                _logger.LogInformation("Validating HTTP-01 challenge for {Hostname}", hostname);
                await httpChallenge.Validate();

                // Wait for challenge validation (with timeout)
                var maxAttempts = 30; // 60 seconds total
                var attempt = 0;
                Challenge? resource = null;
                
                while (attempt < maxAttempts)
                {
                    await Task.Delay(2000, cancellationToken);
                    
                    resource = await httpChallenge.Resource();
                    _logger.LogDebug(
                        "Challenge status for {Hostname}: {Status}",
                        hostname, resource.Status);

                    if (resource.Status == ChallengeStatus.Valid)
                    {
                        _logger.LogInformation("HTTP-01 challenge validated successfully for {Hostname}", hostname);
                        break;
                    }

                    if (resource.Status == ChallengeStatus.Invalid)
                    {
                        _logger.LogError(
                            "HTTP-01 challenge validation failed for {Hostname}: {Error}",
                            hostname, resource.Error?.Detail ?? "Unknown error");
                        return null;
                    }

                    attempt++;
                }

                if (attempt >= maxAttempts)
                {
                    _logger.LogError(
                        "HTTP-01 challenge validation timed out for {Hostname}. Last status: {Status}",
                        hostname, resource?.Status);
                    return null;
                }

                // Generate private key for the certificate
                _logger.LogInformation("Generating certificate private key for {Hostname}", hostname);
                var privateKey = KeyFactory.NewKey(KeyAlgorithm.ES256);

                // Complete the order and download certificate
                _logger.LogInformation("Finalizing order and downloading certificate for {Hostname}", hostname);
                var certChain = await order.Generate(new CsrInfo
                {
                    CountryName = "US",
                    State = "State",
                    Locality = "City",
                    Organization = "Harbor Gate",
                    OrganizationUnit = "IT",
                    CommonName = hostname,
                }, privateKey);

                // Convert to X509Certificate2 with private key
                var pfxBuilder = certChain.ToPfx(privateKey);
                
                // For Pebble testing, skip full chain validation if configured
                if (_options.SkipAcmeServerCertificateValidation)
                {
                    _logger.LogDebug("Skipping full certificate chain validation for Pebble testing");
                    pfxBuilder.FullChain = false;
                }
                
                var pfxBytes = pfxBuilder.Build(hostname, string.Empty);
                var certificate = X509CertificateLoader.LoadPkcs12(pfxBytes, null, X509KeyStorageFlags.Exportable);

                _logger.LogInformation(
                    "Successfully obtained Let's Encrypt certificate for {Hostname}, expires: {ExpiresAt}",
                    hostname, certificate.NotAfter);

                return certificate;
            }
            finally
            {
                // Clean up challenge token
                _challengeStore.RemoveChallenge(token);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to request certificate from Let's Encrypt for {Hostname}", hostname);
            return null;
        }
    }

    /// <summary>
    /// Gets or creates an ACME context for Let's Encrypt
    /// </summary>
    private async Task<AcmeContext> GetAcmeContextAsync(CancellationToken cancellationToken)
    {
        if (_acmeContext != null)
        {
            return _acmeContext;
        }

        await _acmeContextLock.WaitAsync(cancellationToken);
        try
        {
            if (_acmeContext != null)
            {
                return _acmeContext;
            }

            // Determine ACME directory URL
            var directoryUrl = _options.AcmeDirectoryUrl;
            if (string.IsNullOrEmpty(directoryUrl))
            {
                directoryUrl = _options.UseStaging
                    ? WellKnownServers.LetsEncryptStagingV2.ToString()
                    : WellKnownServers.LetsEncryptV2.ToString();
            }

            _logger.LogInformation(
                "Initializing ACME context with directory: {DirectoryUrl}",
                directoryUrl);

            // Create custom HttpClient for Pebble testing if needed
            HttpClient? httpClient = null;
            IAcmeHttpClient? acmeHttpClient = null;
            
            if (_options.SkipAcmeServerCertificateValidation)
            {
                _logger.LogWarning(
                    "SSL certificate validation is DISABLED for ACME server connections. " +
                    "This should ONLY be used for testing with Pebble or similar test ACME servers!");
                
                // Create HttpClient with SSL validation bypass
                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) =>
                    {
                        _logger.LogDebug(
                            "Bypassing SSL validation for ACME server certificate. Errors: {Errors}",
                            errors);
                        return true; // Accept all certificates
                    }
                };
                
                httpClient = new HttpClient(handler);
                acmeHttpClient = new AcmeHttpClient(new Uri(directoryUrl), httpClient);
            }

            // Create new ACME context
            _acmeContext = new AcmeContext(new Uri(directoryUrl), null, acmeHttpClient);

            // Create or load account
            var account = await _acmeContext.NewAccount(_options.Email, termsOfServiceAgreed: true);
            
            _logger.LogInformation(
                "ACME account initialized for {Email}",
                _options.Email);

            return _acmeContext;
        }
        finally
        {
            _acmeContextLock.Release();
        }
    }
}
