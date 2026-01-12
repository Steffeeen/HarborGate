using HarborGate.Services;
using HarborGate.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace HarborGate.Middleware;

/// <summary>
/// Middleware that conditionally enforces authentication based on route configuration
/// Routes with harborgate.auth.enable=true require authentication
/// </summary>
public class ConditionalAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ConditionalAuthenticationMiddleware> _logger;

    public ConditionalAuthenticationMiddleware(
        RequestDelegate next,
        ILogger<ConditionalAuthenticationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        RouteConfigurationService routeService,
        IAuthorizationService authorizationService)
    {
        // Get the requested host
        var host = context.Request.Host.Host;

        _logger.LogDebug("ConditionalAuthenticationMiddleware: Processing request for host: {Host}, Path: {Path}", 
            host, context.Request.Path);

        // Find the route configuration for this host
        var route = routeService.GetAllRoutes().Values
            .FirstOrDefault(r => r.Host.Equals(host, StringComparison.OrdinalIgnoreCase));

        if (route == null)
        {
            _logger.LogWarning("No route found for host: {Host}", host);
            await _next(context);
            return;
        }

        _logger.LogDebug("Route found: {RouteId}, Auth enabled: {AuthEnabled}, Auth config: {AuthConfig}", 
            route.Id, route.Auth?.Enable, route.Auth != null ? "present" : "null");

        // If no route found or auth not enabled, continue without authentication check
        if (route?.Auth?.Enable != true)
        {
            _logger.LogDebug("No authentication required for host: {Host}", host);
            await _next(context);
            return;
        }

        _logger.LogDebug("Authentication IS REQUIRED for host: {Host}, Required roles: {Roles}",
            host, route.Auth.Roles != null ? string.Join(", ", route.Auth.Roles) : "none");

        // Check if user is authenticated
        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            _logger.LogDebug("User not authenticated for protected route: {Host}", host);
            
            // Trigger authentication challenge
            await context.ChallengeAsync();
            return;
        }

        // User is authenticated - now check roles if required
        if (route.Auth.Roles != null && route.Auth.Roles.Length > 0)
        {
            var requirement = new RoleRequirement(route.Auth.Roles);
            var authResult = await authorizationService.AuthorizeAsync(
                context.User,
                null,
                requirement);

            if (!authResult.Succeeded)
            {
                _logger.LogWarning("User {User} lacks required roles for host: {Host}. Required: {RequiredRoles}",
                    context.User.Identity?.Name ?? "unknown",
                    host,
                    string.Join(", ", route.Auth.Roles));

                // Return 403 Forbidden
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "Forbidden",
                    message = "You do not have the required roles to access this resource"
                });
                return;
            }

            _logger.LogDebug("User {User} authorized for host: {Host}", context.User.Identity?.Name ?? "unknown", host);
        }

        // Authentication and authorization successful, continue to next middleware
        await _next(context);
    }
}

/// <summary>
/// Extension methods for ConditionalAuthenticationMiddleware
/// </summary>
public static class ConditionalAuthenticationMiddlewareExtensions
{
    public static IApplicationBuilder UseConditionalAuthentication(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ConditionalAuthenticationMiddleware>();
    }
}
