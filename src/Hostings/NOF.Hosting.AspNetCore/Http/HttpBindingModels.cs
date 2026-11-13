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

/// <summary>One immutable, describable HTTP input.</summary>
public sealed record HttpInputDescription(string Name, HttpInputLocation Location, Type ValueType, bool Required, object? DefaultValue = null, string? ContentType = null);

/// <summary>One actual response shape, after inheritance and projection.</summary>
public sealed record HttpResponseDescription(Type SourceType, int StatusCode, Type? BodyType, string? ContentType);

/// <summary>The final HTTP contract used by both execution and OpenAPI.</summary>
public sealed record HttpEndpointBinding(ServiceOperationDescriptor Operation, IReadOnlyList<HttpInputDescription> Inputs, IReadOnlyList<HttpResponseDescription> Responses, IReadOnlyList<Type> FailureCases);

/// <summary>Describes an externally supplied opaque input or writer without probing delegates.</summary>
public sealed record HttpBodyDescription(Type BodyType, string ContentType = "application/json", bool Required = true);
