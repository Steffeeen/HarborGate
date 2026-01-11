using HarborGate.Configuration;
using Microsoft.AspNetCore.Http.Extensions;

namespace HarborGate.Middleware;

/// <summary>
/// Middleware that redirects HTTP requests to HTTPS
/// </summary>
public class HttpsRedirectMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<HttpsRedirectMiddleware> _logger;
    private readonly HarborGateOptions _options;

    public HttpsRedirectMiddleware(
        RequestDelegate next,
        ILogger<HttpsRedirectMiddleware> logger,
        HarborGateOptions options)
    {
        _next = next;
        _logger = logger;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only redirect if:
        // 1. HTTPS is enabled
        // 2. Redirect is enabled
        // 3. Request is HTTP (not HTTPS)
        // 4. Request is not for ACME challenge (HTTP-01 must be served over HTTP)
        // 5. Request is not for health check endpoint
        if (_options.EnableHttps &&
            _options.RedirectHttpToHttps &&
            !context.Request.IsHttps &&
            !IsAcmeChallenge(context.Request.Path) &&
            !IsHealthCheck(context.Request.Path))
        {
            var httpsUrl = BuildHttpsUrl(context.Request);
            
            _logger.LogDebug(
                "Redirecting HTTP request to HTTPS: {OriginalUrl} -> {HttpsUrl}",
                context.Request.GetDisplayUrl(),
                httpsUrl);

            context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
            context.Response.Headers.Location = httpsUrl;
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// Check if the request is for an ACME HTTP-01 challenge
    /// ACME challenges must be served over HTTP, not redirected to HTTPS
    /// </summary>
    private static bool IsAcmeChallenge(PathString path)
    {
        return path.StartsWithSegments("/.well-known/acme-challenge");
    }

    /// <summary>
    /// Check if the request is for the health check endpoint
    /// Health checks should work over HTTP for Docker healthcheck compatibility
    /// </summary>
    private static bool IsHealthCheck(PathString path)
    {
        return path.StartsWithSegments("/_health");
    }

    /// <summary>
    /// Build the HTTPS URL from the HTTP request
    /// </summary>
    private string BuildHttpsUrl(HttpRequest request)
    {
        var host = request.Host;
        
        // If using non-standard HTTPS port, include it in the redirect
        // Standard port 443 doesn't need to be included
        if (_options.HttpsPort != 443)
        {
            host = new HostString(host.Host, _options.HttpsPort);
        }
        else
        {
            // Remove port if it was explicitly specified as 80
            host = new HostString(host.Host);
        }

        return $"https://{host}{request.PathBase}{request.Path}{request.QueryString}";
    }
}

/// <summary>
/// Extension methods for registering the HTTPS redirect middleware
/// </summary>
public static class HttpsRedirectMiddlewareExtensions
{
    public static IApplicationBuilder UseHttpsRedirect(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<HttpsRedirectMiddleware>();
    }
}
