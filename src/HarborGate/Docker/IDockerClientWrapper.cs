using Docker.DotNet.Models;
using HarborGate.Models;

namespace HarborGate.Docker;

/// <summary>
/// Abstraction over Docker client for testing and flexibility
/// </summary>
public interface IDockerClientWrapper
{
    /// <summary>
    /// Inspects a container and extracts Harbor Gate configuration
    /// </summary>
    Task<ContainerInfo?> InspectContainerAsync(string containerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all running containers with Harbor Gate labels
    /// </summary>
    Task<IEnumerable<ContainerInfo>> ListContainersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Monitors Docker events and invokes callback for relevant events
    /// </summary>
    Task MonitorEventsAsync(
        Func<string, string, Task> onContainerEvent,
        CancellationToken cancellationToken = default);
}
