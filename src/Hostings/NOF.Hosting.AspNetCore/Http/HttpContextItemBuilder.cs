using Microsoft.AspNetCore.Http;
using NOF.Abstraction;
using System.Text.Json.Nodes;

namespace NOF.Hosting.AspNetCore;

/// <summary>Configures one explicit semantic context source.</summary>
public sealed class HttpContextItemBuilder(HttpContextBuilder parent, string key)
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
    public HttpContextBuilder FromValue(JsonNode? value)
    {
        var snapshot = Context.Empty.WithItem(key, value)[key];
        parent.Items[key] = _ => snapshot;
        parent.Inputs.Remove(key);
        return parent;
    }

    /// <summary>Uses an opaque HTTP provider with an explicit external input contract.</summary>
    public HttpContextBuilder FromHttp(Func<HttpContext, JsonNode?> provider, params HttpInputDescription[] inputs)
    {
        parent.Items[key] = http => Context.Empty.WithItem(key, provider(http))[key];
        parent.Inputs[key] = Array.AsReadOnly(inputs.ToArray());
        return parent;
    }

}
