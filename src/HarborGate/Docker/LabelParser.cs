using HarborGate.Models;

namespace HarborGate.Docker;

/// <summary>
/// Parses Harbor Gate labels from Docker container labels
/// </summary>
public static class LabelParser
{
    private const string LabelPrefix = "harborgate.";
    private const string EnableLabel = "harborgate.enable";
    private const string HostLabel = "harborgate.host";
    private const string PortLabel = "harborgate.port";
    private const string TlsLabel = "harborgate.tls";
    private const string AuthEnableLabel = "harborgate.auth.enable";
    private const string AuthRolesLabel = "harborgate.auth.roles";

    /// <summary>
    /// Parses Harbor Gate labels from a container's label dictionary
    /// </summary>
    public static HarborGateLabels Parse(IDictionary<string, string> labels)
    {
        var config = new HarborGateLabels();

        if (labels == null || labels.Count == 0)
        {
            return config;
        }

        // Parse enable flag
        if (labels.TryGetValue(EnableLabel, out var enableValue))
        {
            config.Enable = ParseBool(enableValue);
        }

        // Parse host
        if (labels.TryGetValue(HostLabel, out var hostValue))
        {
            config.Host = hostValue;
        }

        // Parse port
        if (labels.TryGetValue(PortLabel, out var portValue) && int.TryParse(portValue, out var port))
        {
            config.Port = port;
        }

        // Parse TLS
        if (labels.TryGetValue(TlsLabel, out var tlsValue))
        {
            config.Tls = ParseBool(tlsValue);
        }

        // Parse authentication config
        var authEnable = labels.TryGetValue(AuthEnableLabel, out var authEnableValue) && ParseBool(authEnableValue);
        
        if (authEnable)
        {
            config.Auth = new AuthConfig
            {
                Enable = true,
                Roles = labels.TryGetValue(AuthRolesLabel, out var rolesValue)
                    ? rolesValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    : null
            };
        }

        return config;
    }

    /// <summary>
    /// Checks if a container has Harbor Gate labels
    /// </summary>
    public static bool HasHarborGateLabels(IDictionary<string, string> labels)
    {
        if (labels == null || labels.Count == 0)
        {
            return false;
        }

        return labels.Keys.Any(key => key.StartsWith(LabelPrefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Parses a boolean value from various string representations
    /// </summary>
    private static bool ParseBool(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}
