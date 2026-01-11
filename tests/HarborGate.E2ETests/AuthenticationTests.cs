using System.Diagnostics;
using System.Net;
using FluentAssertions;
using Xunit.Abstractions;

namespace HarborGate.E2ETests;

[Collection("Sequential")]
public class AuthenticationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly string _composeFile = "docker-compose.auth.yml";
    private readonly string _projectName = "harborgate-auth-test";
    private readonly HttpClient _httpClient;
    private KeycloakHelper? _keycloakHelper;

    public AuthenticationTests(ITestOutputHelper output)
    {
        _output = output;
        
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false
        };
        
        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:8080"),
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public async Task InitializeAsync()
    {
        _output.WriteLine("Starting authentication test environment...");
        
        // Stop any existing containers
        await RunDockerComposeCommand("down -v");
        
        // Start the test environment
        await RunDockerComposeCommand("up -d --build");
        
        // Wait for services to be healthy
        await WaitForHealthCheck();
        
        _output.WriteLine("Waiting for Keycloak to be ready...");
        await Task.Delay(30000); // Keycloak takes time to start
        
        // Configure Keycloak
        _output.WriteLine("Configuring Keycloak...");
        await RunBashScript("setup-keycloak.sh");
        
        await Task.Delay(5000);
        
        _keycloakHelper = new KeycloakHelper();
        
        _output.WriteLine("Authentication test environment ready");
    }

    public async Task DisposeAsync()
    {
        _output.WriteLine("Cleaning up authentication test environment...");
        await RunDockerComposeCommand("down -v");
        _httpClient.Dispose();
        _keycloakHelper?.Dispose();
    }

    [Fact]
    public async Task Test01_HarborGate_HealthCheck_ReturnsSuccess()
    {
        // Act
        var response = await _httpClient.GetAsync("/_health");
        var content = await response.Content.ReadAsStringAsync();
        
        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("Harbor Gate");
        content.Should().Contain("\"oidc\":true");
        
        _output.WriteLine($"Health check response: {content}");
    }

    [Fact]
    public async Task Test02_PublicRoute_DoesNotRequireAuthentication()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "public.auth.test";

        // Act
        var response = await _httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("Hostname:");
        
        _output.WriteLine("Public route accessible without authentication ✓");
    }

    [Fact]
    public async Task Test03_ProtectedRoute_RedirectsToAuthentication()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "protected.auth.test";

        // Act
        var response = await _httpClient.SendAsync(request);

        // Assert
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Redirect,
            HttpStatusCode.Found,
            HttpStatusCode.TemporaryRedirect,
            HttpStatusCode.Unauthorized
        );
        
        _output.WriteLine($"Protected route requires authentication: {response.StatusCode}");
        
        // Check if redirect goes to Keycloak
        if (response.Headers.Location != null)
        {
            var location = response.Headers.Location.ToString();
            _output.WriteLine($"Redirect location: {location}");
            location.Should().Contain("keycloak");
        }
    }

    [Fact]
    public async Task Test04_AdminRoute_RedirectsToAuthentication()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "admin.auth.test";

        // Act
        var response = await _httpClient.SendAsync(request);

        // Assert
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.Redirect,
            HttpStatusCode.Found,
            HttpStatusCode.TemporaryRedirect,
            HttpStatusCode.Unauthorized,
            HttpStatusCode.Forbidden
        );
        
        _output.WriteLine($"Admin route requires authentication: {response.StatusCode}");
    }

    [Fact]
    public async Task Test05_Keycloak_CanAuthenticateAdminUser()
    {
        // This test verifies Keycloak is configured correctly
        
        // Act
        var token = await _keycloakHelper!.GetAccessToken("admin-user", "admin123");

        // Assert
        token.Should().NotBeNullOrEmpty();
        
        var isValid = await _keycloakHelper.ValidateToken(token);
        isValid.Should().BeTrue();
        
        _output.WriteLine("Successfully authenticated admin user with Keycloak ✓");
        _output.WriteLine($"Token (first 50 chars): {token.Substring(0, Math.Min(50, token.Length))}...");
    }

    [Fact]
    public async Task Test06_Keycloak_CanAuthenticateRegularUser()
    {
        // This test verifies Keycloak is configured correctly
        
        // Act
        var token = await _keycloakHelper!.GetAccessToken("regular-user", "user123");

        // Assert
        token.Should().NotBeNullOrEmpty();
        
        var isValid = await _keycloakHelper.ValidateToken(token);
        isValid.Should().BeTrue();
        
        _output.WriteLine("Successfully authenticated regular user with Keycloak ✓");
    }

    [Fact]
    public async Task Test07_Keycloak_RejectsInvalidCredentials()
    {
        // Act & Assert
        Func<Task> act = async () => await _keycloakHelper!.GetAccessToken("admin-user", "wrongpassword");
        
        await act.Should().ThrowAsync<Exception>();
        
        _output.WriteLine("Keycloak correctly rejects invalid credentials ✓");
    }

    [Fact]
    public async Task Test08_MultipleRoutes_RespectAuthConfiguration()
    {
        // Test that different routes have different auth requirements
        
        // Public route - should work
        var publicRequest = new HttpRequestMessage(HttpMethod.Get, "/");
        publicRequest.Headers.Host = "public.auth.test";
        var publicResponse = await _httpClient.SendAsync(publicRequest);
        publicResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        
        // Protected route - should redirect
        var protectedRequest = new HttpRequestMessage(HttpMethod.Get, "/");
        protectedRequest.Headers.Host = "protected.auth.test";
        var protectedResponse = await _httpClient.SendAsync(protectedRequest);
        protectedResponse.StatusCode.Should().NotBe(HttpStatusCode.OK);
        
        // Admin route - should redirect
        var adminRequest = new HttpRequestMessage(HttpMethod.Get, "/");
        adminRequest.Headers.Host = "admin.auth.test";
        var adminResponse = await _httpClient.SendAsync(adminRequest);
        adminResponse.StatusCode.Should().NotBe(HttpStatusCode.OK);
        
        _output.WriteLine("All routes correctly implement their auth requirements ✓");
    }

    private async Task RunDockerComposeCommand(string command)
    {
        // Get the solution root directory (HarborGate/)
        var currentDir = Directory.GetCurrentDirectory();
        var testsDir = Path.GetFullPath(Path.Combine(currentDir, "../../../../../tests"));
        var composeFilePath = Path.Combine(testsDir, _composeFile);
        
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = $"compose -f {composeFilePath} -p {_projectName} {command}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(startInfo);
        if (process == null)
            throw new Exception("Failed to start docker-compose");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (!string.IsNullOrWhiteSpace(output))
            _output.WriteLine($"Docker Compose output: {output}");
        if (!string.IsNullOrWhiteSpace(error) && !error.Contains("Warning"))
            _output.WriteLine($"Docker Compose error: {error}");

        if (process.ExitCode != 0 && !command.Contains("down"))
        {
            throw new Exception($"Docker Compose failed with exit code {process.ExitCode}");
        }
    }
    
    private async Task RunDockerCommand(string command)
    {
        // Get the solution root directory (HarborGate/)
        var currentDir = Directory.GetCurrentDirectory();
        var testsDir = Path.GetFullPath(Path.Combine(currentDir, "../../../../../tests"));
        
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = command,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(startInfo);
        if (process == null)
            throw new Exception("Failed to start docker-compose");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (!string.IsNullOrWhiteSpace(output))
            _output.WriteLine($"docker {command} output: {output}");
        if (!string.IsNullOrWhiteSpace(error) && !error.Contains("Warning"))
            _output.WriteLine($"docker {command} error: {error}");

        if (process.ExitCode != 0 && !command.Contains("down"))
        {
            throw new Exception($"Docker command failed with exit code {process.ExitCode}");
        }
    }

    private async Task RunBashScript(string scriptPath)
    {
        var currentDir = Directory.GetCurrentDirectory();
        var testsDir = Path.GetFullPath(Path.Combine(currentDir, "../../../../../tests"));
        var fullPath = Path.Combine(testsDir, scriptPath);
        
        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = fullPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.Environment["KEYCLOAK_URL"] = "http://localhost:8090";

        var process = Process.Start(startInfo);
        if (process == null)
            throw new Exception("Failed to start bash script");

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        _output.WriteLine($"Script output: {output}");
        if (!string.IsNullOrWhiteSpace(error))
            _output.WriteLine($"Script errors: {error}");

        if (process.ExitCode != 0)
        {
            throw new Exception($"Script failed with exit code {process.ExitCode}");
        }
    }

    private async Task WaitForHealthCheck()
    {
        var maxAttempts = 120;
        var attempt = 0;

        while (attempt < maxAttempts)
        {
            try
            {
                var response = await _httpClient.GetAsync("/_health");
                if (response.IsSuccessStatusCode)
                {
                    _output.WriteLine("Harbor Gate is healthy!");
                    return;
                }
            }
            catch
            {
                // Ignore connection errors during startup
            }

            attempt++;
            await Task.Delay(1000);
            
            if (attempt % 10 == 0)
                _output.WriteLine($"Waiting for Harbor Gate... ({attempt}/{maxAttempts})");
        }

        await RunDockerCommand("logs harborgate-auth-test");
        throw new Exception("Harbor Gate failed to become healthy");
    }
}
