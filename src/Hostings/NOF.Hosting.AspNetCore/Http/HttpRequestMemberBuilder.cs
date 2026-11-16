using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing.Patterns;
using NOF.Abstraction;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace NOF.Hosting.AspNetCore;

/// <summary>Assigns a declared source to a single request member.</summary>
public sealed class HttpRequestMemberBuilder<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] T, TValue>(HttpRequestBuilder<T> parent, string name)
{
    /// <summary>Binds a native route parameter.</summary>
    public HttpRequestBuilder<T> FromRoute(string source) => Set(HttpInputLocation.Route, source);
    /// <summary>Binds a query parameter.</summary>
    public HttpRequestBuilder<T> FromQuery(string source) => Set(HttpInputLocation.Query, source);
    /// <summary>Binds a query parameter with a documented default.</summary>
    public HttpRequestBuilder<T> FromQuery(string source, TValue defaultValue) => Set(HttpInputLocation.Query, source, true, defaultValue);
    /// <summary>Binds one header.</summary>
    public HttpRequestBuilder<T> FromHeader(string source) => Set(HttpInputLocation.Header, source);
    /// <summary>Binds one top-level JSON property.</summary>
    public HttpRequestBuilder<T> FromBody(string source) => Set(HttpInputLocation.Body, source);
    private HttpRequestBuilder<T> Set(HttpInputLocation source, string sourceName, bool hasDefault = false, object? defaultValue = null)
    {
        parent.Members[name] = new(name, source, sourceName, typeof(TValue), hasDefault, defaultValue);
        return parent;
    }
}
