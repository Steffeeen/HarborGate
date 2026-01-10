using System.Collections.Concurrent;

namespace HarborGate.Certificates;

/// <summary>
/// In-memory store for ACME HTTP-01 challenge responses
/// </summary>
public class HttpChallengeStore : IHttpChallengeStore
{
    private readonly ConcurrentDictionary<string, string> _challenges = new();
    private readonly ILogger<HttpChallengeStore> _logger;

    public HttpChallengeStore(ILogger<HttpChallengeStore> logger)
    {
        _logger = logger;
    }

    public void AddChallenge(string token, string keyAuthorization)
    {
        _challenges[token] = keyAuthorization;
        _logger.LogInformation("Added ACME challenge token: {Token}", token);
    }

    public string? GetKeyAuthorization(string token)
    {
        return _challenges.TryGetValue(token, out var keyAuthz) ? keyAuthz : null;
    }

    public void RemoveChallenge(string token)
    {
        if (_challenges.TryRemove(token, out _))
        {
            _logger.LogInformation("Removed ACME challenge token: {Token}", token);
        }
    }
}
