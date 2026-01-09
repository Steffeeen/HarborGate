using Docker.DotNet;
using Docker.DotNet.Models;
using HarborGate.Models;

namespace HarborGate.Docker;

/// <summary>
/// Wrapper around Docker.DotNet client for container inspection and monitoring
/// </summary>
public class DockerClientWrapper : IDockerClientWrapper, IDisposable
{
    private readonly DockerClient _client;
    private readonly ILogger<DockerClientWrapper> _logger;
    private readonly bool _runningInContainer;

    public DockerClientWrapper(string dockerSocket, ILogger<DockerClientWrapper> logger)
    {
        _logger = logger;
        
        // Create Docker client with Unix socket
        var config = new DockerClientConfiguration(new Uri($"unix://{dockerSocket}"));
        _client = config.CreateClient();
        
        // Detect if we're running inside a Docker container
        _runningInContainer = File.Exists("/.dockerenv");
        
        _logger.LogInformation(
            "Docker client initialized with socket: {Socket}. Running in container: {InContainer}",
            dockerSocket, _runningInContainer);
    }

    /// <inheritdoc/>
    public async Task<ContainerInfo?> InspectContainerAsync(string containerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var container = await _client.Containers.InspectContainerAsync(containerId, cancellationToken);
            
            if (container?.Config?.Labels == null)
            {
                return null;
            }

            var labels = LabelParser.Parse(container.Config.Labels);
            
            // Only process containers with harborgate.enable=true
            if (!labels.Enable)
            {
                return null;
            }

            // Validate required fields
            if (string.IsNullOrWhiteSpace(labels.Host))
            {
                _logger.LogWarning(
                    "Container {ContainerId} has harborgate.enable=true but no host configured",
                    containerId);
                return null;
            }

            // Discover port
            var targetPort = DiscoverPort(container, labels);
            if (targetPort == null)
            {
                _logger.LogWarning(
                    "Container {ContainerId} has no exposed ports and no port label specified",
                    containerId);
                return null;
            }

            // Determine IP address and port based on where Harbor Gate is running
            string ipAddress;
            int actualPort;

            if (_runningInContainer)
            {
                // Running in container: use container IP and internal port
                var networkSettings = container.NetworkSettings?.Networks?.FirstOrDefault();
                if (networkSettings?.Value == null || string.IsNullOrWhiteSpace(networkSettings.Value.Value.IPAddress))
                {
                    _logger.LogWarning(
                        "Container {ContainerId} has no network configuration or IP address",
                        containerId);
                    return null;
                }

                ipAddress = networkSettings.Value.Value.IPAddress;
                actualPort = targetPort.Value;

                _logger.LogDebug(
                    "Using container network address for {ContainerId}: {IpAddress}:{Port}",
                    containerId, ipAddress, actualPort);
            }
            else
            {
                // Running on host: use localhost and published port
                var publishedPort = GetPublishedPort(container, targetPort.Value);
                if (publishedPort == null)
                {
                    _logger.LogWarning(
                        "Container {ContainerId} port {InternalPort} is not published to host. " +
                        "For local development, containers must publish ports using -p flag.",
                        containerId, targetPort.Value);
                    return null;
                }

                ipAddress = "127.0.0.1"; // localhost when running on host
                actualPort = publishedPort.Value;

                _logger.LogDebug(
                    "Using host published port for {ContainerId}: localhost:{Port} (container port: {InternalPort})",
                    containerId, actualPort, targetPort.Value);
            }

            var network = container.NetworkSettings?.Networks?.FirstOrDefault().Key ?? "unknown";

            return new ContainerInfo
            {
                Id = container.ID,
                Name = container.Name?.TrimStart('/') ?? containerId,
                IpAddress = ipAddress,
                Labels = labels,
                TargetPort = actualPort,
                Network = network,
                IsRunning = container.State?.Running ?? false
            };
        }
        catch (DockerContainerNotFoundException)
        {
            _logger.LogDebug("Container {ContainerId} not found", containerId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inspecting container {ContainerId}", containerId);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<ContainerInfo>> ListContainersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var containers = await _client.Containers.ListContainersAsync(
                new ContainersListParameters { All = false },
                cancellationToken);

            var result = new List<ContainerInfo>();

            foreach (var container in containers)
            {
                var containerInfo = await InspectContainerAsync(container.ID, cancellationToken);
                if (containerInfo != null)
                {
                    result.Add(containerInfo);
                }
            }

            _logger.LogInformation("Found {Count} containers with Harbor Gate labels", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing containers");
            return Array.Empty<ContainerInfo>();
        }
    }

    /// <inheritdoc/>
    public async Task MonitorEventsAsync(
        Func<string, string, Task> onContainerEvent,
        CancellationToken cancellationToken = default)
    {
        var parameters = new ContainerEventsParameters
        {
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["type"] = new Dictionary<string, bool> { ["container"] = true },
                ["event"] = new Dictionary<string, bool>
                {
                    ["start"] = true,
                    ["die"] = true,
                    ["stop"] = true,
                    ["destroy"] = true
                }
            }
        };

        _logger.LogInformation("Starting Docker event monitoring");

        try
        {
            var progress = new Progress<Message>(async message =>
            {
                if (message.Type == "container" && !string.IsNullOrEmpty(message.ID))
                {
                    _logger.LogDebug(
                        "Docker event: {Action} for container {ContainerId}",
                        message.Action, message.ID);
                    
                    await onContainerEvent(message.ID, message.Action);
                }
            });

            await _client.System.MonitorEventsAsync(parameters, progress, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Docker event monitoring stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error monitoring Docker events");
            throw;
        }
    }

    /// <summary>
    /// Discovers the target port for a container
    /// Priority: 1) harborgate.port label, 2) First exposed port
    /// </summary>
    private int? DiscoverPort(ContainerInspectResponse container, HarborGateLabels labels)
    {
        // Check if port is explicitly specified in labels
        if (labels.Port.HasValue)
        {
            _logger.LogDebug(
                "Using explicit port {Port} from label for container {ContainerId}",
                labels.Port.Value, container.ID);
            return labels.Port.Value;
        }

        // Auto-discover from exposed ports
        var exposedPorts = container.Config?.ExposedPorts?.Keys.ToList();
        
        if (exposedPorts == null || exposedPorts.Count == 0)
        {
            return null;
        }

        // Parse first exposed port (format: "8080/tcp")
        var firstPort = exposedPorts.First();
        var portString = firstPort.Split('/')[0];
        
        if (int.TryParse(portString, out var port))
        {
            _logger.LogDebug(
                "Auto-discovered port {Port} for container {ContainerId}",
                port, container.ID);
            
            if (exposedPorts.Count > 1)
            {
                _logger.LogWarning(
                    "Container {ContainerId} exposes multiple ports. Using first port {Port}. " +
                    "Consider setting harborgate.port label explicitly.",
                    container.ID, port);
            }
            
            return port;
        }

        return null;
    }

    /// <summary>
    /// Gets the published (host) port for a container's internal port
    /// </summary>
    private int? GetPublishedPort(ContainerInspectResponse container, int internalPort)
    {
        // Check HostConfig.PortBindings for the published port
        var portBindings = container.HostConfig?.PortBindings;
        if (portBindings == null || portBindings.Count == 0)
        {
            return null;
        }

        // Look for the internal port in various formats
        var portKey = $"{internalPort}/tcp";
        if (portBindings.TryGetValue(portKey, out var bindings) && bindings != null && bindings.Count > 0)
        {
            var hostPort = bindings[0].HostPort;
            if (int.TryParse(hostPort, out var port))
            {
                return port;
            }
        }

        return null;
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}
