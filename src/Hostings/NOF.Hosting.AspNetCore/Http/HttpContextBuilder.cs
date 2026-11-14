using Microsoft.AspNetCore.Http;
using NOF.Abstraction;

namespace NOF.Hosting.AspNetCore;

/// <summary>Explicit HTTP-to-semantic context rules. Identity remains host owned.</summary>
public sealed class HttpContextBuilder
{
    /// <summary>Gets immutable-key context providers.</summary>
    public Dictionary<object, Func<HttpContext, object?>> Items { get; } = new();
    /// <summary>Gets the explicitly configured tenant source.</summary>
    public Func<HttpContext, string?>? Tenant { get; private set; }
    /// <summary>Gets external context input metadata.</summary>
    public Dictionary<object, IReadOnlyList<HttpInputDescription>> Inputs { get; } = new();
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
    public HttpContextItemBuilder Item(object key)
    {
        if (key is string text && string.Equals(text, NOFAbstractionConstants.Transport.Headers.TenantId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Use an explicit tenant source.", nameof(key));
        }

        return new(this, key);
    }
}

/// <summary>Configures one explicit semantic context source.</summary>
public sealed class HttpContextItemBuilder(HttpContextBuilder parent, object key)
{
    /// <summary>Reads one HTTP header.</summary>
    public HttpContextBuilder FromHeader(string name)
    {
        parent.Inputs.Remove(key);
        parent.Items[key] = http => http.Request.Headers[name].ToString();
        parent.Inputs[key] = [new(name, HttpInputLocation.Header, typeof(string), false)];
        return parent;
    }

    /// <summary>Reads a route value.</summary>
    public HttpContextBuilder FromRoute(string name)
    {
        parent.Items[key] = http => http.Request.RouteValues[name]?.ToString();
        parent.Inputs[key] = [new(name, HttpInputLocation.Route, typeof(string), true)];
        return parent;
    }

    /// <summary>Uses a semantic constant.</summary>
    public HttpContextBuilder FromValue<T>(T value)
    {
        var snapshot = Snapshot(value);
        parent.Items[key] = _ => snapshot;
        parent.Inputs.Remove(key);
        return parent;
    }

    /// <summary>Uses an opaque HTTP provider with an explicit external input contract.</summary>
    public HttpContextBuilder FromHttp(Func<HttpContext, object?> provider, params HttpInputDescription[] inputs)
    {
        parent.Items[key] = http => Snapshot(provider(http));
        parent.Inputs[key] = Array.AsReadOnly(inputs.ToArray());
        return parent;
    }

    private static object? Snapshot(object? value)
    {
        if (value is null or string or Uri or Guid or DateTime or DateTimeOffset or DateOnly or TimeOnly or TimeSpan or decimal || value.GetType().IsPrimitive || value.GetType().IsEnum)
        {
            return value;
        }

        throw new InvalidOperationException("HTTP semantic context values must be immutable scalars. Put returned business data in the outcome.");
    }
}
