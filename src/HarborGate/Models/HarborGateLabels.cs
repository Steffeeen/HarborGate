namespace HarborGate.Models;

/// <summary>
/// Represents Harbor Gate configuration labels from a Docker container
/// </summary>
public class HarborGateLabels
{
    /// <summary>
    /// Whether Harbor Gate should proxy this container. Label: harborgate.enable
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    /// The host/domain to route to this container. Label: harborgate.host
    /// </summary>
    public string? Host { get; set; }

    /// <summary>
    /// The port to connect to in the container. Label: harborgate.port
    /// If not specified, the first exposed port will be auto-discovered.
    /// </summary>
    public int? Port { get; set; }

    /// <summary>
    /// Whether to enable TLS for this route. Label: harborgate.tls
    /// Default: true if host is specified
    /// </summary>
    public bool Tls { get; set; } = true;

    /// <summary>
    /// Authentication configuration. Label prefix: harborgate.auth.*
    /// </summary>
    public AuthConfig? Auth { get; set; }
}

/// <summary>
/// Authentication configuration for a route
/// </summary>
public class AuthConfig
{
    /// <summary>
    /// Whether authentication is required. Label: harborgate.auth.enable
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    /// Required roles for access (comma-separated). Label: harborgate.auth.roles
    /// </summary>
    public string[]? Roles { get; set; }
}
