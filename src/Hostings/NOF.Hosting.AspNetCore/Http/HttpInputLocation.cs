using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using System.Diagnostics.CodeAnalysis;

namespace NOF.Hosting.AspNetCore;

/// <summary>The declared native location of an HTTP input.</summary>
public enum HttpInputLocation
{
    /// <summary>A route parameter.</summary>
    Route,
    /// <summary>A query parameter.</summary>
    Query,
    /// <summary>An explicitly selected request header.</summary>
    Header,
    /// <summary>A top-level JSON body member.</summary>
    Body,
    /// <summary>The entire JSON body.</summary>
    WholeBody
}
