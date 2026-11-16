using Microsoft.AspNetCore.Http;
using NOF.Abstraction;
using System.Text.Json.Nodes;

namespace NOF.Hosting.AspNetCore;

/// <summary>Explicit HTTP-to-semantic context rules. Identity remains host owned.</summary>
public sealed class HttpContextBuilder
{
    /// <summary>Gets immutable-key context providers.</summary>
    public Dictionary<string, Func<HttpContext, JsonNode?>> Items { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Gets the explicitly configured tenant source.</summary>
    public Func<HttpContext, string?>? Tenant { get; private set; }
    /// <summary>Gets external context input metadata.</summary>
    public Dictionary<string, IReadOnlyList<HttpInputDescription>> Inputs { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Gets tenant input metadata, if the source is external.</summary>
    public HttpInputDescription? TenantInput { get; private set; }

    /// <summary>Reads a tenant only from an authenticated native principal.</summary>
    public HttpContextBuilder TenantFromClaim(string claim)
    {
        TenantInput = null;
        Tenant = http => http.User.Identity?.IsAuthenticated == true ? http.User.FindFirst(claim)?.Value : null;
        return this;
    }

    /// <summary>Reads untrusted tenant intent; invocation policies still validate it.</summary>
    public HttpContextBuilder TenantFromHeader(string name)
    {
        Tenant = http => http.Request.Headers[name].ToString();
        TenantInput = new(name, HttpInputLocation.Header, typeof(string), false);
        return this;
    }

    /// <summary>Configures one semantic key.</summary>
    public HttpContextItemBuilder Item(string key)
    {
        if (string.Equals(key, NOFAbstractionConstants.Transport.Headers.TenantId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Use an explicit tenant source.", nameof(key));
        }

        return new(this, key);
    }
}
