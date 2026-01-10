namespace HarborGate.Certificates;

/// <summary>
/// Interface for storing HTTP-01 challenge responses
/// Used during ACME certificate validation
/// </summary>
public interface IHttpChallengeStore
{
    /// <summary>
    /// Adds a challenge token and its response
    /// </summary>
    void AddChallenge(string token, string keyAuthorization);

    /// <summary>
    /// Gets the key authorization for a challenge token
    /// </summary>
    string? GetKeyAuthorization(string token);

    /// <summary>
    /// Removes a challenge token
    /// </summary>
    void RemoveChallenge(string token);
}
