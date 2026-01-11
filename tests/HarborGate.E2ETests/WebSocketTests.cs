using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using FluentAssertions;
using Xunit.Abstractions;

namespace HarborGate.E2ETests;

[Collection("Sequential")]
public class WebSocketTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly string _composeFile = "docker-compose.websocket.yml";
    private readonly string _projectName = "harborgate-websocket-test";
    private readonly HttpClient _httpClient;

    public WebSocketTests(ITestOutputHelper output)
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
        _output.WriteLine("Starting WebSocket test environment...");
        
        // Stop any existing containers
        await RunDockerComposeCommand("down -v");
        
        // Start the test environment
        await RunDockerComposeCommand("up -d --build");
        
        // Wait for Harbor Gate to be healthy
        await WaitForHealthCheck();
        
        // Give services a moment to settle
        await Task.Delay(3000);
        
        _output.WriteLine("WebSocket test environment ready");
    }

    public async Task DisposeAsync()
    {
        _output.WriteLine("Cleaning up WebSocket test environment...");
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
    public async Task Test02_WebSocket_BasicConnection_ConnectsSuccessfully()
    {
        // Arrange
        var wsUrl = "ws://localhost:8080";
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Host", "ws.test.local");

        // Act - Connect
        await ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);

        // Assert
        ws.State.Should().Be(WebSocketState.Open);
        
        _output.WriteLine($"WebSocket connected successfully. State: {ws.State}");

        // Cleanup
        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test complete", CancellationToken.None);
    }

    [Fact]
    public async Task Test03_WebSocket_SendAndReceive_EchoesMessage()
    {
        // Arrange
        var wsUrl = "ws://localhost:8080";
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Host", "ws.test.local");
        await ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);

        var testMessage = "Hello WebSocket!";
        var sendBuffer = Encoding.UTF8.GetBytes(testMessage);
        var receiveBuffer = new byte[1024];

        // Act - Send message
        await ws.SendAsync(
            new ArraySegment<byte>(sendBuffer), 
            WebSocketMessageType.Text, 
            endOfMessage: true, 
            CancellationToken.None);

        // Act - Receive echo
        var result = await ws.ReceiveAsync(
            new ArraySegment<byte>(receiveBuffer), 
            CancellationToken.None);

        // Assert
        result.MessageType.Should().Be(WebSocketMessageType.Text);
        result.EndOfMessage.Should().BeTrue();
        
        var receivedMessage = Encoding.UTF8.GetString(receiveBuffer, 0, result.Count);
        receivedMessage.Should().Contain("Request served by"); // Echo server response format
        
        _output.WriteLine($"Sent: {testMessage}");
        _output.WriteLine($"Received: {receivedMessage.Substring(0, Math.Min(200, receivedMessage.Length))}");

        // Cleanup
        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test complete", CancellationToken.None);
    }

    [Fact]
    public async Task Test04_WebSocket_MultipleMessages_AllEchoed()
    {
        // Arrange
        var wsUrl = "ws://localhost:8080";
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Host", "ws.test.local");
        await ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);

        var messages = new[] { "Message 1", "Message 2", "Message 3" };
        var receiveBuffer = new byte[4096];

        // Act & Assert - Send and receive multiple messages
        foreach (var message in messages)
        {
            var sendBuffer = Encoding.UTF8.GetBytes(message);
            
            await ws.SendAsync(
                new ArraySegment<byte>(sendBuffer), 
                WebSocketMessageType.Text, 
                endOfMessage: true, 
                CancellationToken.None);

            var result = await ws.ReceiveAsync(
                new ArraySegment<byte>(receiveBuffer), 
                CancellationToken.None);

            result.MessageType.Should().Be(WebSocketMessageType.Text);
            
            var receivedMessage = Encoding.UTF8.GetString(receiveBuffer, 0, result.Count);
            _output.WriteLine($"Sent: {message}, Received: {receivedMessage.Substring(0, Math.Min(100, receivedMessage.Length))}");
        }

        // Cleanup
        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test complete", CancellationToken.None);
    }

    [Fact]
    public async Task Test05_WebSocket_LongLivedConnection_RemainsOpen()
    {
        // Arrange
        var wsUrl = "ws://localhost:8080";
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Host", "ws.test.local");
        await ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);

        // Act - Keep connection open for 10 seconds and send periodic messages
        for (int i = 0; i < 5; i++)
        {
            var message = $"Ping {i + 1}";
            var sendBuffer = Encoding.UTF8.GetBytes(message);
            var receiveBuffer = new byte[1024];

            await ws.SendAsync(
                new ArraySegment<byte>(sendBuffer), 
                WebSocketMessageType.Text, 
                endOfMessage: true, 
                CancellationToken.None);

            var result = await ws.ReceiveAsync(
                new ArraySegment<byte>(receiveBuffer), 
                CancellationToken.None);

            // Assert - Connection remains open
            ws.State.Should().Be(WebSocketState.Open);
            result.MessageType.Should().Be(WebSocketMessageType.Text);

            _output.WriteLine($"Message {i + 1}/5 sent and received successfully");

            await Task.Delay(2000); // Wait 2 seconds between messages
        }

        // Assert - Connection still open after 10 seconds
        ws.State.Should().Be(WebSocketState.Open);
        _output.WriteLine("WebSocket connection remained stable for 10 seconds");

        // Cleanup
        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Test complete", CancellationToken.None);
    }

    [Fact]
    public async Task Test06_WebSocket_InvalidHost_FailsToConnect()
    {
        // Arrange
        var wsUrl = "ws://localhost:8080";
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Host", "invalid.host.local");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<WebSocketException>(async () =>
        {
            await ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);
        });

        _output.WriteLine($"WebSocket correctly rejected invalid host: {exception.Message}");
    }

    [Fact]
    public async Task Test07_WebSocket_GracefulClose_ClosesCleanly()
    {
        // Arrange
        var wsUrl = "ws://localhost:8080";
        using var ws = new ClientWebSocket();
        ws.Options.SetRequestHeader("Host", "ws.test.local");
        await ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);

        // Act - Close connection gracefully
        await ws.CloseAsync(
            WebSocketCloseStatus.NormalClosure, 
            "Test closing gracefully", 
            CancellationToken.None);

        // Assert
        ws.State.Should().Be(WebSocketState.Closed);
        ws.CloseStatus.Should().Be(WebSocketCloseStatus.NormalClosure);
        
        _output.WriteLine($"WebSocket closed gracefully. Status: {ws.CloseStatus}, Description: {ws.CloseStatusDescription}");
    }

    private async Task WaitForHealthCheck()
    {
        var maxAttempts = 60;
        var delay = TimeSpan.FromSeconds(2);

        for (int i = 0; i < maxAttempts; i++)
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
                // Ignore and retry
            }

            await Task.Delay(delay);
        }

        throw new Exception("Harbor Gate failed to become healthy within timeout period");
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
}
