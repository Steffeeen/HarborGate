namespace HarborGate.Configuration;

public class HarborGateOptions
{
    public const string SectionName = "HarborGate";

    /// <summary>
    /// Path to Docker socket. Default: /var/run/docker.sock
    /// </summary>
    public string DockerSocket { get; set; } = "/var/run/docker.sock";

    /// <summary>
    /// HTTP port to listen on. Default: 80
    /// </summary>
    public int HttpPort { get; set; } = 80;

    /// <summary>
    /// HTTPS port to listen on. Default: 443
    /// </summary>
    public int HttpsPort { get; set; } = 443;

    /// <summary>
    /// Whether to enable HTTPS. Default: true
    /// </summary>
    public bool EnableHttps { get; set; } = true;

    /// <summary>
    /// Log level for the application. Default: Information
    /// </summary>
    public string LogLevel { get; set; } = "Information";

    /// <summary>
    /// SSL/TLS configuration
    /// </summary>
    public SslOptions Ssl { get; set; } = new();
}

public class SslOptions
{
    /// <summary>
    /// Certificate provider type: SelfSigned, LetsEncrypt
    /// </summary>
    public string CertificateProvider { get; set; } = "SelfSigned";

    /// <summary>
    /// Directory to store certificates. Default: /var/lib/harborgate/certs
    /// </summary>
    public string CertificateStoragePath { get; set; } = "/var/lib/harborgate/certs";

    /// <summary>
    /// Let's Encrypt configuration
    /// </summary>
    public LetsEncryptOptions LetsEncrypt { get; set; } = new();
}

public class LetsEncryptOptions
{
    /// <summary>
    /// Email address for Let's Encrypt account
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Use Let's Encrypt staging environment for testing. Default: false
    /// </summary>
    public bool UseStaging { get; set; } = false;

    /// <summary>
    /// Custom ACME directory URL (for Pebble testing)
    /// </summary>
    public string? AcmeDirectoryUrl { get; set; }

    /// <summary>
    /// Accept Let's Encrypt Terms of Service. Must be true to use Let's Encrypt.
    /// </summary>
    public bool AcceptTermsOfService { get; set; } = false;

    /// <summary>
    /// Skip SSL certificate validation when connecting to ACME server (for Pebble testing). Default: false
    /// WARNING: This should only be enabled for local testing with Pebble!
    /// </summary>
    public bool SkipAcmeServerCertificateValidation { get; set; } = false;
}
