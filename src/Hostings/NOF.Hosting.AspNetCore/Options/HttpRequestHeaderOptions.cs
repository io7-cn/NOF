using NOF.Abstraction;

namespace NOF.Hosting.AspNetCore;

/// <summary>
/// Selects HTTP request headers admitted into the application execution context.
/// </summary>
public class HttpRequestHeaderOptions
{
    /// <summary>
    /// Gets the inbound HTTP header name patterns available to application policies.
    /// The asterisk character matches any sequence of characters.
    /// </summary>
    public List<string> AllowedHeaders { get; set; } =
    [
        NOFAbstractionConstants.Transport.Headers.Authorization,
        "NOF.*",
        "X-*"
    ];
}
