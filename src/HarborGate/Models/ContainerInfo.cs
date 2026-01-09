namespace HarborGate.Models;

/// <summary>
/// Represents information about a Docker container relevant to Harbor Gate
/// </summary>
public class ContainerInfo
{
    /// <summary>
    /// Docker container ID
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Container name
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Container's IP address (on the Docker network)
    /// </summary>
    public required string IpAddress { get; set; }

    /// <summary>
    /// Harbor Gate configuration labels
    /// </summary>
    public required HarborGateLabels Labels { get; set; }

    /// <summary>
    /// The target port to proxy to (after auto-discovery or from label)
    /// </summary>
    public required int TargetPort { get; set; }

    /// <summary>
    /// Docker network the container is connected to
    /// </summary>
    public required string Network { get; set; }

    /// <summary>
    /// Whether the container is currently running
    /// </summary>
    public bool IsRunning { get; set; }
}
