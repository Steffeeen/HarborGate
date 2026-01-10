using System.Text.Json;
using HarborGate.Configuration;

namespace HarborGate.Services;

/// <summary>
/// Validates OIDC provider configuration and connectivity at startup
/// </summary>
public class OidcProviderValidator
{
    private readonly ILogger<OidcProviderValidator> _logger;
    private readonly HttpClient _httpClient;

    public OidcProviderValidator(ILogger<OidcProviderValidator> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Validates the OIDC provider configuration
    /// </summary>
    /// <param name="options">OIDC configuration options</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result with success status and error messages</returns>
    public async Task<OidcValidationResult> ValidateAsync(OidcOptions options, CancellationToken cancellationToken = default)
    {
        var result = new OidcValidationResult();

        // Basic configuration validation
        if (string.IsNullOrWhiteSpace(options.Authority))
        {
            result.Errors.Add("OIDC Authority is not configured. Set HARBORGATE_OIDC_AUTHORITY environment variable.");
            return result;
        }

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            result.Errors.Add("OIDC Client ID is not configured. Set HARBORGATE_OIDC_CLIENT_ID environment variable.");
            return result;
        }

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            result.Errors.Add("OIDC Client Secret is not configured. Set HARBORGATE_OIDC_CLIENT_SECRET environment variable.");
            return result;
        }

        // Validate Authority URL format
        if (!Uri.TryCreate(options.Authority, UriKind.Absolute, out var authorityUri))
        {
            result.Errors.Add($"OIDC Authority is not a valid URL: {options.Authority}");
            return result;
        }

        // Validate HTTPS requirement
        if (options.RequireHttpsMetadata && authorityUri.Scheme != "https")
        {
            result.Errors.Add($"OIDC Authority must use HTTPS: {options.Authority}. Set HARBORGATE_OIDC_REQUIRE_HTTPS_METADATA=false for development only.");
            return result;
        }

        if (!options.RequireHttpsMetadata && authorityUri.Scheme != "https")
        {
            result.Warnings.Add($"OIDC Authority is using HTTP instead of HTTPS: {options.Authority}. This should only be used for development/testing!");
        }

        // Fetch and validate OpenID Connect discovery document
        try
        {
            var discoveryUrl = BuildDiscoveryUrl(options.Authority);
            _logger.LogInformation("Fetching OIDC discovery document from: {DiscoveryUrl}", discoveryUrl);

            var response = await _httpClient.GetAsync(discoveryUrl, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                result.Errors.Add($"Failed to fetch OIDC discovery document. Status: {response.StatusCode}. URL: {discoveryUrl}");
                result.Errors.Add("Ensure the OIDC Authority URL is correct and the provider is accessible.");
                return result;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            
            // Parse and validate the discovery document
            var discoveryDoc = JsonDocument.Parse(content);
            result.DiscoveryDocument = discoveryDoc;

            // Validate required endpoints exist
            if (!ValidateDiscoveryDocument(discoveryDoc, result))
            {
                return result;
            }

            _logger.LogInformation("Successfully validated OIDC provider configuration");
            result.IsValid = true;
        }
        catch (HttpRequestException ex)
        {
            result.Errors.Add($"Failed to connect to OIDC provider: {ex.Message}");
            result.Errors.Add($"Authority: {options.Authority}");
            result.Errors.Add("Check network connectivity and ensure the OIDC provider is accessible from this container/server.");
            
            if (ex.InnerException != null)
            {
                result.Errors.Add($"Inner error: {ex.InnerException.Message}");
            }
        }
        catch (TaskCanceledException)
        {
            result.Errors.Add($"Timeout while connecting to OIDC provider: {options.Authority}");
            result.Errors.Add("The provider might be down or network connectivity is poor.");
        }
        catch (JsonException ex)
        {
            result.Errors.Add($"Failed to parse OIDC discovery document: {ex.Message}");
            result.Errors.Add("The discovery endpoint returned invalid JSON. Verify the Authority URL is correct.");
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Unexpected error while validating OIDC provider: {ex.Message}");
        }

        return result;
    }

    private bool ValidateDiscoveryDocument(JsonDocument doc, OidcValidationResult result)
    {
        var root = doc.RootElement;
        var requiredEndpoints = new[] 
        { 
            "authorization_endpoint", 
            "token_endpoint", 
            "userinfo_endpoint",
            "jwks_uri"
        };

        foreach (var endpoint in requiredEndpoints)
        {
            if (!root.TryGetProperty(endpoint, out var endpointValue) || 
                string.IsNullOrWhiteSpace(endpointValue.GetString()))
            {
                result.Errors.Add($"OIDC discovery document is missing required endpoint: {endpoint}");
            }
        }

        // Check for issuer
        if (!root.TryGetProperty("issuer", out var issuer) || 
            string.IsNullOrWhiteSpace(issuer.GetString()))
        {
            result.Errors.Add("OIDC discovery document is missing 'issuer' field");
        }

        return result.Errors.Count == 0;
    }

    private string BuildDiscoveryUrl(string authority)
    {
        // Ensure authority doesn't have trailing slash for consistency
        var cleanAuthority = authority.TrimEnd('/');
        
        // Standard OIDC discovery endpoint
        return $"{cleanAuthority}/.well-known/openid-configuration";
    }
}

/// <summary>
/// Result of OIDC provider validation
/// </summary>
public class OidcValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public JsonDocument? DiscoveryDocument { get; set; }
}
