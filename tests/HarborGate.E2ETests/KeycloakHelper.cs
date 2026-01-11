using System.Text.Json;

namespace HarborGate.E2ETests;

public class KeycloakHelper
{
    private readonly string _keycloakUrl;
    private readonly string _realm;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly HttpClient _httpClient;

    public KeycloakHelper(
        string keycloakUrl = "http://localhost:8090",
        string realm = "harborgate",
        string clientId = "harborgate-test",
        string clientSecret = "test-secret-12345")
    {
        _keycloakUrl = keycloakUrl;
        _realm = realm;
        _clientId = clientId;
        _clientSecret = clientSecret;
        _httpClient = new HttpClient();
        // Set default headers for JSON responses and a sensible timeout
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<string> GetAccessToken(string username, string password)
    {
        var tokenEndpoint = $"{_keycloakUrl}/realms/{_realm}/protocol/openid-connect/token";
        
        // Build form content - include client_secret only if present (confidential client)
        var formFields = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "password"),
            new("client_id", _clientId),
            new("username", username),
            new("password", password),
            // scope is optional, keep it minimal to avoid server-side validation quirks
            new("scope", "openid")
        };
        if (!string.IsNullOrWhiteSpace(_clientSecret))
        {
            // For confidential clients, Keycloak accepts client_secret in the form body
            formFields.Add(new KeyValuePair<string, string>("client_secret", _clientSecret));
        }

        var content = new FormUrlEncodedContent(formFields);

        var response = await _httpClient.PostAsync(tokenEndpoint, content);

        // Provide detailed diagnostics on failure (e.g., 400 Bad Request)
        var raw = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            try
            {
                var errJson = JsonSerializer.Deserialize<JsonElement>(raw);
                var error = errJson.TryGetProperty("error", out var e) ? e.GetString() : null;
                var description = errJson.TryGetProperty("error_description", out var d) ? d.GetString() : null;
                throw new Exception($"Keycloak token request failed: {(int)response.StatusCode} {response.ReasonPhrase}. error={error}, description={description}");
            }
            catch
            {
                throw new Exception($"Keycloak token request failed: {(int)response.StatusCode} {response.ReasonPhrase}. Response: {raw}");
            }
        }

        var tokenResponse = JsonSerializer.Deserialize<JsonElement>(raw);
        
        return tokenResponse.GetProperty("access_token").GetString() 
               ?? throw new Exception("No access token in response");
    }

    public async Task<bool> ValidateToken(string accessToken)
    {
        try
        {
            var userInfoEndpoint = $"{_keycloakUrl}/realms/{_realm}/protocol/openid-connect/userinfo";
            
            var request = new HttpRequestMessage(HttpMethod.Get, userInfoEndpoint);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
