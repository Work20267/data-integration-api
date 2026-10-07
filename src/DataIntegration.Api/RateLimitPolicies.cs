namespace DataIntegration.Api;

/// <summary>
/// Named rate-limiting policies, declared once so a controller attribute and the
/// registration cannot drift apart — a typo in either would silently disable the
/// limit rather than fail.
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>
    /// Endpoints that cost real work: an upstream fetch, or a full batch
    /// transformation.
    /// </summary>
    public const string Expensive = "expensive";
}
