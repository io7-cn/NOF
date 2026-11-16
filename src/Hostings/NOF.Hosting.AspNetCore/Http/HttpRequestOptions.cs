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

/// <summary>Options merged independently through native route groups.</summary>
public sealed class HttpRequestOptions
{
    /// <summary>Gets or sets explicitly overridden route name comparison.</summary>
    public StringComparison? RouteNameComparison { get; set; }
    /// <summary>Gets or sets explicitly overridden query name comparison.</summary>
    public StringComparison? QueryNameComparison { get; set; }
}
