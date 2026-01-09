using HarborGate.Certificates;

namespace HarborGate.Middleware;

/// <summary>
/// Middleware that handles ACME HTTP-01 challenge requests
/// Responds to requests at /.well-known/acme-challenge/{token}
/// </summary>
public class AcmeChallengeMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IHttpChallengeStore _challengeStore;
    private readonly ILogger<AcmeChallengeMiddleware> _logger;
    private const string AcmeChallengePath = "/.well-known/acme-challenge/";

    public AcmeChallengeMiddleware(
        RequestDelegate next,
        IHttpChallengeStore challengeStore,
        ILogger<AcmeChallengeMiddleware> logger)
    {
        _next = next;
        _challengeStore = challengeStore;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value;

        // Check if this is an ACME challenge request
        if (path?.StartsWith(AcmeChallengePath, StringComparison.OrdinalIgnoreCase) == true)
        {
            var token = path.Substring(AcmeChallengePath.Length);
            
            _logger.LogDebug("ACME challenge request for token: {Token}", token);

            var keyAuthorization = _challengeStore.GetKeyAuthorization(token);
            
            if (keyAuthorization != null)
            {
                _logger.LogInformation("Responding to ACME challenge for token: {Token}", token);
                
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync(keyAuthorization);
                return;
            }
            else
            {
                _logger.LogWarning("ACME challenge token not found: {Token}", token);
                context.Response.StatusCode = 404;
                return;
            }
        }

        // Not an ACME challenge, continue to next middleware
        await _next(context);
    }
}

/// <summary>
/// Extension methods for adding ACME challenge middleware
/// </summary>
public static class AcmeChallengeMiddlewareExtensions
{
    public static IApplicationBuilder UseAcmeChallenge(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<AcmeChallengeMiddleware>();
    }
}
