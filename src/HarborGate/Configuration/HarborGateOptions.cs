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
    /// Whether to automatically redirect HTTP to HTTPS. Default: true when HTTPS is enabled
    /// </summary>
    public bool RedirectHttpToHttps { get; set; } = true;

    /// <summary>
    /// Log level for the application. Default: Information
    /// </summary>
    public string LogLevel { get; set; } = "Information";

    /// <summary>
    /// SSL/TLS configuration
    /// </summary>
    public SslOptions Ssl { get; set; } = new();

    /// <summary>
    /// OpenID Connect authentication configuration
    /// </summary>
    public OidcOptions Oidc { get; set; } = new();
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

public class OidcOptions
{
    /// <summary>
    /// OpenID Connect Authority URL (e.g., https://accounts.google.com or https://login.microsoftonline.com/{tenant}/v2.0)
    /// </summary>
    public string Authority { get; set; } = string.Empty;

    /// <summary>
    /// OAuth 2.0 Client ID
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// OAuth 2.0 Client Secret
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Callback path for authentication. Default: /signin-oidc
    /// </summary>
    public string CallbackPath { get; set; } = "/signin-oidc";

    /// <summary>
    /// Scopes to request. Default: openid, profile, email
    /// </summary>
    public string[] Scopes { get; set; } = new[] { "openid", "profile", "email" };

    /// <summary>
    /// Name of the claim that contains user roles. Default: roles
    /// </summary>
    public string RoleClaimType { get; set; } = "roles";

    /// <summary>
    /// Whether to save tokens (access token, refresh token) in authentication properties. Default: false
    /// </summary>
    public bool SaveTokens { get; set; } = false;

    /// <summary>
    /// Whether OIDC is enabled globally. Default: false
    /// Individual routes can still require authentication via labels.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Allow HTTP (non-HTTPS) for OIDC authority during development. Default: false
    /// WARNING: Only set to true for local development/testing! Production should always use HTTPS.
    /// </summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>
    /// Skip OIDC provider validation on startup. Default: false
    /// WARNING: Only set to true for E2E testing where OIDC provider might not be ready at startup!
    /// </summary>
    public bool SkipValidation { get; set; } = false;
}
