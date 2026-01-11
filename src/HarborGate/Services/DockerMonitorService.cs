using HarborGate.Configuration;
using HarborGate.Docker;
using HarborGate.Models;

namespace HarborGate.Services;

/// <summary>
/// Background service that monitors Docker containers and updates route configuration
/// </summary>
public class DockerMonitorService : BackgroundService
{
    private readonly IDockerClientWrapper _dockerClient;
    private readonly RouteConfigurationService _routeService;
    private readonly ILogger<DockerMonitorService> _logger;
    private readonly HarborGateOptions _options;

    public DockerMonitorService(
        IDockerClientWrapper dockerClient,
        RouteConfigurationService routeService,
        ILogger<DockerMonitorService> logger,
        HarborGateOptions options)
    {
        _dockerClient = dockerClient;
        _routeService = routeService;
        _logger = logger;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Docker Monitor Service starting");

        try
        {
            // Initial scan: Load existing containers
            await ScanExistingContainersAsync(stoppingToken);

            // Start monitoring for container events
            await _dockerClient.MonitorEventsAsync(OnContainerEventAsync, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Docker Monitor Service stopping");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in Docker Monitor Service");
            throw;
        }
    }

    /// <summary>
    /// Scans all existing running containers and adds them to routing
    /// </summary>
    private async Task ScanExistingContainersAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Scanning existing containers");

        var containers = await _dockerClient.ListContainersAsync(cancellationToken);

        foreach (var container in containers)
        {
            await AddOrUpdateContainerRouteAsync(container);
        }

        _logger.LogInformation(
            "Initial container scan complete. Found {Count} containers with Harbor Gate configuration",
            containers.Count());
    }

    /// <summary>
    /// Handles Docker container events
    /// </summary>
    private async Task OnContainerEventAsync(string containerId, string action)
    {
        try
        {
            switch (action)
            {
                case "start":
                    _logger.LogDebug("Container started: {ContainerId}", containerId);
                    await HandleContainerStartAsync(containerId);
                    break;

                case "die":
                case "stop":
                case "destroy":
                    _logger.LogDebug("Container stopped/removed: {ContainerId}", containerId);
                    await HandleContainerStopAsync(containerId);
                    break;

                default:
                    _logger.LogDebug("Ignoring event {Action} for container {ContainerId}", action, containerId);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling container event {Action} for {ContainerId}", action, containerId);
        }
    }

    /// <summary>
    /// Handles container start event
    /// </summary>
    private async Task HandleContainerStartAsync(string containerId)
    {
        // Small delay to ensure container is fully started
        await Task.Delay(500);

        var container = await _dockerClient.InspectContainerAsync(containerId);
        
        if (container != null)
        {
            await AddOrUpdateContainerRouteAsync(container);
        }
    }

    /// <summary>
    /// Handles container stop/removal event
    /// </summary>
    private Task HandleContainerStopAsync(string containerId)
    {
        _routeService.RemoveRoute(containerId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Adds or updates a route for a container
    /// </summary>
    private Task AddOrUpdateContainerRouteAsync(ContainerInfo container)
    {
        var destinationUrl = $"http://{container.IpAddress}:{container.TargetPort}";

        var route = new RouteConfiguration
        {
            Id = container.Id,
            Host = container.Labels.Host!,
            DestinationUrl = destinationUrl,
            TlsEnabled = container.Labels.Tls,
            Auth = container.Labels.Auth
        };

        _routeService.AddOrUpdateRoute(route);

        _logger.LogInformation(
            "Route configured for container {ContainerName} ({ContainerId}): {Host} -> {Destination}",
            container.Name, container.Id[..12], route.Host, route.DestinationUrl);

        // Warn if authentication is required but OIDC is not enabled globally
        if (route.Auth?.Enable == true && !_options.Oidc.Enabled)
        {
            _logger.LogWarning(
                "Container {ContainerName} ({ContainerId}) has harborgate.auth.enable=true, but OIDC is DISABLED globally (HARBORGATE_OIDC_ENABLED=false). " +
                "This route will NOT be protected and will allow unauthenticated access!",
                container.Name, container.Id[..12]);
        }

        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Docker Monitor Service stopping gracefully");
        await base.StopAsync(cancellationToken);
    }
}
