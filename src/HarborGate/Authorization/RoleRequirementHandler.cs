using Microsoft.AspNetCore.Authorization;

namespace HarborGate.Authorization;

/// <summary>
/// Authorization handler for role-based access control
/// Checks if the user has at least one of the required roles
/// </summary>
public class RoleRequirementHandler : AuthorizationHandler<RoleRequirement>
{
    private readonly ILogger<RoleRequirementHandler> _logger;
    private readonly string _roleClaimType;

    public RoleRequirementHandler(ILogger<RoleRequirementHandler> logger, string roleClaimType = "roles")
    {
        _logger = logger;
        _roleClaimType = roleClaimType;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        RoleRequirement requirement)
    {
        // If no roles are required, succeed
        if (requirement.RequiredRoles == null || requirement.RequiredRoles.Length == 0)
        {
            _logger.LogDebug("No roles required, allowing access");
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // Get user's roles from claims
        var userRoles = context.User.Claims
            .Where(c => c.Type == _roleClaimType || c.Type == System.Security.Claims.ClaimTypes.Role)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _logger.LogDebug("User has roles: {UserRoles}", string.Join(", ", userRoles));
        _logger.LogDebug("Required roles (any): {RequiredRoles}", string.Join(", ", requirement.RequiredRoles));

        // Check if user has at least one of the required roles
        var hasRequiredRole = requirement.RequiredRoles.Any(
            requiredRole => userRoles.Contains(requiredRole));

        if (hasRequiredRole)
        {
            _logger.LogDebug("User has required role, allowing access");
            context.Succeed(requirement);
        }
        else
        {
            _logger.LogWarning("User does not have any of the required roles. User: {User}, Required: {RequiredRoles}",
                context.User.Identity?.Name ?? "anonymous",
                string.Join(", ", requirement.RequiredRoles));
            // Don't call Fail() - just don't succeed, allowing other handlers to run
        }

        return Task.CompletedTask;
    }
}
