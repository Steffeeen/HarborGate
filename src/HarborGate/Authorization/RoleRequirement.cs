using Microsoft.AspNetCore.Authorization;

namespace HarborGate.Authorization;

/// <summary>
/// Authorization requirement for role-based access control
/// </summary>
public class RoleRequirement : IAuthorizationRequirement
{
    /// <summary>
    /// Required roles for access (any one of these roles grants access)
    /// </summary>
    public string[] RequiredRoles { get; }

    public RoleRequirement(string[] requiredRoles)
    {
        RequiredRoles = requiredRoles ?? Array.Empty<string>();
    }
}
