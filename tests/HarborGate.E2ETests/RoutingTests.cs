using System.Diagnostics;
using System.Net;
using FluentAssertions;
using Xunit.Abstractions;

namespace HarborGate.E2ETests;

[Collection("Sequential")]
public class RoutingTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly string _composeFile = "docker-compose.routing.yml";
    private readonly string _projectName = "harborgate-routing-test";
    private readonly HttpClient _httpClient;

    public RoutingTests(ITestOutputHelper output)
    {
        _output = output;
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:8080"),
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public async Task InitializeAsync()
    {
        _output.WriteLine("Starting routing test environment...");
        
        // Stop any existing containers
        await RunDockerComposeCommand("down -v");
        
        // Start the test environment
        await RunDockerComposeCommand("up -d --build");
        
        // Wait for Harbor Gate to be healthy
        await WaitForHealthCheck();
        
        // Give services a moment to settle
        await Task.Delay(3000);
        
        _output.WriteLine("Test environment ready");
    }

    public async Task DisposeAsync()
    {
        _output.WriteLine("Cleaning up routing test environment...");
        await RunDockerComposeCommand("down -v");
        _httpClient.Dispose();
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
        content.Should().Contain("running");
        
        _output.WriteLine($"Health check response: {content}");
    }

    [Fact]
    public async Task Test02_App1_BasicRouting_ReturnsWhoamiResponse()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "app1.test.local";

        // Act
        var response = await _httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("Hostname:");
        content.Should().Contain("GET / HTTP");
        
        _output.WriteLine($"App1 response: {content.Substring(0, Math.Min(200, content.Length))}...");
    }

    [Fact]
    public async Task Test03_App2_ExplicitPortRouting_ReturnsWhoamiResponse()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "app2.test.local";

        // Act
        var response = await _httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("Hostname:");
        
        _output.WriteLine($"App2 response: {content.Substring(0, Math.Min(200, content.Length))}...");
    }

    [Fact]
    public async Task Test04_App3_NginxService_ReturnsNginxResponse()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "app3.test.local";

        // Act
        var response = await _httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("nginx");
        
        _output.WriteLine($"App3 response: {content.Substring(0, Math.Min(200, content.Length))}...");
    }

    [Fact]
    public async Task Test05_UnknownHost_Returns404NotFound()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "unknown.test.local";

        // Act
        var response = await _httpClient.SendAsync(request);

        // Assert
        // YARP returns 404 when no route matches the host, not 503
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        
        _output.WriteLine($"Unknown host returned: {response.StatusCode}");
    }

    [Fact]
    public async Task Test06_DynamicContainerAddition_CreatesRoute()
    {
        // Arrange - Start a new container dynamically
        var containerName = "dynamic-test-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        var hostname = $"{containerName}.test.local";
        
        var dockerCommand = $"run -d --name {containerName} " +
                           $"--network harborgate-routing-test_harborgate-test " +
                           $"--label harborgate.enable=true " +
                           $"--label harborgate.host={hostname} " +
                           $"--label harborgate.tls=false " +
                           $"traefik/whoami";
        
        await RunDockerCommand(dockerCommand);
        
        // Give Harbor Gate time to detect the new container
        await Task.Delay(3000);

        try
        {
            // Act
            var request = new HttpRequestMessage(HttpMethod.Get, "/");
            request.Headers.Host = hostname;
            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            content.Should().Contain("Hostname:");
            
            _output.WriteLine($"Dynamic container route works! Response: {content.Substring(0, Math.Min(200, content.Length))}...");
        }
        finally
        {
            // Cleanup
            await RunDockerCommand($"rm -f {containerName}");
        }
    }

    [Fact]
    public async Task Test07_ContainerRemoval_RemovesRoute()
    {
        // Arrange - Verify app1 works first
        var request1 = new HttpRequestMessage(HttpMethod.Get, "/");
        request1.Headers.Host = "app1.test.local";
        var response1 = await _httpClient.SendAsync(request1);
        response1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act - Stop the container
        await RunDockerCommand("stop test-app-1");
        await Task.Delay(3000); // Give Harbor Gate time to detect removal

        // Assert - Route should no longer work
        var request2 = new HttpRequestMessage(HttpMethod.Get, "/");
        request2.Headers.Host = "app1.test.local";
        var response2 = await _httpClient.SendAsync(request2);
        
        // YARP returns 404 when no route matches, not 503
        response2.StatusCode.Should().Be(HttpStatusCode.NotFound);
        
        _output.WriteLine("Route correctly removed after container stop");

        // Cleanup - Restart for other tests
        await RunDockerCommand("start test-app-1");
        await Task.Delay(3000);
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
            throw new Exception("Failed to start docker");

        await process.WaitForExitAsync();
    }

    private async Task WaitForHealthCheck()
    {
        var maxAttempts = 60;
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

        throw new Exception("Harbor Gate failed to become healthy");
    }
}
