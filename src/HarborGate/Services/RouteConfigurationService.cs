using System.Collections.Concurrent;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;
using HarborGate.Models;

namespace HarborGate.Services;

/// <summary>
/// Manages dynamic route configuration for YARP reverse proxy
/// Implements IProxyConfigProvider to provide hot-reloadable configuration
/// </summary>
public class RouteConfigurationService : IProxyConfigProvider
{
    private readonly ConcurrentDictionary<string, RouteConfiguration> _routes = new();
    private readonly ILogger<RouteConfigurationService> _logger;
    private volatile InMemoryConfig _config;
    private CancellationTokenSource _changeTokenSource = new();

    public RouteConfigurationService(ILogger<RouteConfigurationService> logger)
    {
        _logger = logger;
        _config = new InMemoryConfig(Array.Empty<RouteConfig>(), Array.Empty<ClusterConfig>(), _changeTokenSource.Token);
    }

    /// <summary>
    /// Gets the current proxy configuration
    /// </summary>
    public IProxyConfig GetConfig()
    {
        _logger.LogDebug("GetConfig called - returning {RouteCount} routes, {ClusterCount} clusters", 
            _config.Routes.Count, _config.Clusters.Count);
        
        foreach (var route in _config.Routes)
        {
            _logger.LogDebug("  Route: {RouteId}, Hosts: {Hosts}", 
                route.RouteId, string.Join(", ", route.Match.Hosts ?? Array.Empty<string>()));
        }
        
        return _config;
    }

    /// <summary>
    /// Adds or updates a route configuration
    /// </summary>
    public void AddOrUpdateRoute(RouteConfiguration route)
    {
        _routes.AddOrUpdate(route.Id, route, (_, _) => route);
        
        var containerDisplay = route.Name != null 
            ? $"{route.Name} ({route.Id[..12]})" 
            : route.Id[..12];
        
        _logger.LogInformation(
            "Route added/updated: {Container} - {Host} -> {Destination}",
            containerDisplay, route.Host, route.DestinationUrl);
        
        RebuildConfiguration();
    }

    /// <summary>
    /// Removes a route configuration
    /// </summary>
    public void RemoveRoute(string routeId)
    {
        if (_routes.TryRemove(routeId, out var route))
        {
            var containerDisplay = route.Name != null 
                ? $"{route.Name} ({routeId[..12]})" 
                : routeId[..12];
            
            _logger.LogInformation(
                "Route removed: {Container} - {Host}",
                containerDisplay, route.Host);
            
            RebuildConfiguration();
        }
    }

    /// <summary>
    /// Gets all current routes
    /// </summary>
    public IReadOnlyDictionary<string, RouteConfiguration> GetAllRoutes()
    {
        return _routes;
    }

    /// <summary>
    /// Rebuilds the YARP configuration from current routes and signals a change
    /// </summary>
    private void RebuildConfiguration()
    {
        var routes = _routes.Values
            .Select(r => r.ToYarpRoute())
            .ToArray();

        var clusters = _routes.Values
            .Select(r => r.ToYarpCluster())
            .ToArray();

        // Signal that configuration has changed BEFORE creating new config
        var oldTokenSource = _changeTokenSource;
        _changeTokenSource = new CancellationTokenSource();
        
        // Create new config with the new change token
        _config = new InMemoryConfig(routes, clusters, _changeTokenSource.Token);
        
        // Cancel old token to notify YARP of the change
        oldTokenSource.Cancel();

        _logger.LogInformation(
            "Configuration rebuilt: {RouteCount} routes, {ClusterCount} clusters",
            routes.Length, clusters.Length);
    }

    /// <summary>
    /// In-memory implementation of IProxyConfig
    /// </summary>
    private class InMemoryConfig : IProxyConfig
    {
        private readonly CancellationChangeToken _changeToken;

        public InMemoryConfig(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters, CancellationToken changeToken)
        {
            Routes = routes;
            Clusters = clusters;
            _changeToken = new CancellationChangeToken(changeToken);
        }

        public IReadOnlyList<RouteConfig> Routes { get; }

        public IReadOnlyList<ClusterConfig> Clusters { get; }

        public IChangeToken ChangeToken => _changeToken;
    }
}
