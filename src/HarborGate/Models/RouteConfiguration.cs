using Yarp.ReverseProxy.Configuration;

namespace HarborGate.Models;

/// <summary>
/// Represents a route configuration for Harbor Gate
/// </summary>
public class RouteConfiguration
{
    /// <summary>
    /// Unique identifier for the route (typically container ID)
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// The host/domain for this route
    /// </summary>
    public required string Host { get; set; }

    /// <summary>
    /// The destination URL (container IP and port)
    /// </summary>
    public required string DestinationUrl { get; set; }

    /// <summary>
    /// Whether TLS is enabled for this route
    /// </summary>
    public bool TlsEnabled { get; set; }

    /// <summary>
    /// Authentication configuration
    /// </summary>
    public AuthConfig? Auth { get; set; }

    /// <summary>
    /// Converts this route configuration to a YARP RouteConfig
    /// </summary>
    public RouteConfig ToYarpRoute()
    {
        return new RouteConfig
        {
            RouteId = Id,
            ClusterId = Id,
            Match = new RouteMatch
            {
                Hosts = new[] { Host }
                // Omit Path to match all paths for this host
            }
        };
    }

    /// <summary>
    /// Creates a YARP ClusterConfig for this route
    /// </summary>
    public ClusterConfig ToYarpCluster()
    {
        return new ClusterConfig
        {
            ClusterId = Id,
            Destinations = new Dictionary<string, DestinationConfig>
            {
                [Id] = new DestinationConfig
                {
                    Address = DestinationUrl
                }
            }
        };
    }
}
