using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Xunit.Abstractions;

namespace HarborGate.E2ETests;

[Collection("Sequential")]
public class SslTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly string _composeFile = "docker-compose.ssl.yml";
    private readonly string _projectName = "harborgate-ssl-test";
    private readonly HttpClient _httpClient;
    private readonly HttpClient _httpsClient;

    public SslTests(ITestOutputHelper output)
    {
        _output = output;
        
        // HTTP client that doesn't follow redirects (for testing redirect behavior)
        var httpHandler = new HttpClientHandler
        {
            AllowAutoRedirect = false
        };
        
        _httpClient = new HttpClient(httpHandler)
        {
            BaseAddress = new Uri("http://localhost:8080"),
            Timeout = TimeSpan.FromSeconds(10)
        };

        // Connect to the local test port while preserving the URI hostname as TLS SNI.
        var httpsHandler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, cancellationToken) =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                await socket.ConnectAsync(IPAddress.Loopback, 8443, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            },
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            }
        };
        
        _httpsClient = new HttpClient(httpsHandler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public async Task InitializeAsync()
    {
        _output.WriteLine("Starting SSL test environment with Pebble...");
        
        // Stop any existing containers
        await RunDockerComposeCommand("down -v");
        
        // Start the test environment
        await RunDockerComposeCommand("up -d --build");
        
        // Wait for Harbor Gate to be healthy
        await WaitForHealthCheck();
        
        // Give Pebble time to be ready
        await Task.Delay(5000);
        
        _output.WriteLine("SSL test environment ready");
    }

    public async Task DisposeAsync()
    {
        _output.WriteLine("Cleaning up SSL test environment...");
        await RunDockerComposeCommand("down -v");
        _httpClient.Dispose();
        _httpsClient.Dispose();
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
        
        _output.WriteLine($"Health check response: {content}");
    }

    [Fact]
    public async Task Test02_HttpsRequest_TriggersCertificateRequest()
    {
        // Arrange
        // Act - First HTTPS request should trigger certificate request from Pebble
        _output.WriteLine("Making first HTTPS request to trigger certificate issuance...");
        
        var response = await SendHttpsRequestAsync("app1.ssl.test");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Should().Contain("Hostname:");
        
        _output.WriteLine($"HTTPS request successful! Response: {content.Substring(0, Math.Min(200, content.Length))}...");
    }

    [Fact]
    public async Task Test03_HttpsRequest_UsesCachedCertificate()
    {
        // Arrange - First request to ensure certificate exists
        await SendHttpsRequestAsync("app1.ssl.test");
        
        // Wait a moment
        await Task.Delay(2000);

        // Act - Second request should use cached certificate
        var stopwatch = Stopwatch.StartNew();
        var response = await SendHttpsRequestAsync("app1.ssl.test");
        stopwatch.Stop();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000); // Should be fast with cached cert
        
        _output.WriteLine($"Cached certificate request took {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Test04_MultipleDomains_GetSeparateCertificates()
    {
        // Arrange & Act - Request certificates for two different domains
        var response1 = await SendHttpsRequestAsync("app1.ssl.test");

        await Task.Delay(2000);

        var response2 = await SendHttpsRequestAsync("app2.ssl.test");

        // Assert
        response1.StatusCode.Should().Be(HttpStatusCode.OK);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
        
        _output.WriteLine("Both domains successfully received certificates");
    }

    [Fact]
    public async Task Test05_CertificateStorage_PersistsToVolume()
    {
        // Arrange - Request a certificate
        await SendHttpsRequestAsync("app1.ssl.test");
        
        await Task.Delay(3000);

        // Act - Check if certificate file exists in volume
        var output = await RunDockerCommand(
            "exec harborgate-ssl-test ls -la /app/certs"
        );

        // Assert
        output.Should().Contain(".pfx");
        
        _output.WriteLine($"Certificate storage contents:\n{output}");
    }

    [Fact]
    public async Task Test06_HttpRequest_WorksWithoutSsl()
    {
        // Arrange - HTTP request to HTTPS-enabled domain should still work
        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "app1.ssl.test";

        // Act
        var response = await _httpClient.SendAsync(request);

        // Assert - Should either redirect or work on HTTP port
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.OK,
            HttpStatusCode.MovedPermanently,
            HttpStatusCode.TemporaryRedirect
        );
        
        _output.WriteLine($"HTTP request status: {response.StatusCode}");
    }

    [Fact]
    public async Task Test07_InvalidDomain_IsRejectedBeforeCertificateIssuance()
    {
        var act = () => SendHttpsRequestAsync("newdomain.ssl.test");

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Test08_ConcurrentConnections_UseOneCertificate()
    {
        var connections = Enumerable.Range(0, 8)
            .Select(_ => GetServerCertificateThumbprintAsync("app1.ssl.test"));

        var thumbprints = await Task.WhenAll(connections);

        thumbprints.Should().OnlyHaveUniqueItems().And.HaveCount(1);
    }

    private Task<HttpResponseMessage> SendHttpsRequestAsync(string hostname)
    {
        return _httpsClient.GetAsync($"https://{hostname}:8443/");
    }

    private static async Task<string> GetServerCertificateThumbprintAsync(string hostname)
    {
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(IPAddress.Loopback, 8443);

        await using var sslStream = new SslStream(
            tcpClient.GetStream(),
            leaveInnerStreamOpen: false,
            (_, _, _, _) => true);
        await sslStream.AuthenticateAsClientAsync(hostname);

        return sslStream.RemoteCertificate!.GetCertHashString();
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

    private async Task<string> RunDockerCommand(string command)
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

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        
        return output;
    }

    private async Task WaitForHealthCheck()
    {
        var maxAttempts = 120; // More time for SSL tests
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
