using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using System.Diagnostics.CodeAnalysis;

namespace NOF.Hosting.AspNetCore;

/// <summary>The HTTP boundary snapshot, never a business context return channel.</summary>
public sealed record HttpOutputContext(HttpContext Http, Context Context, ServiceOperationDescriptor Operation, CancellationToken CancellationToken)
{
    /// <summary>Builds a path for a named native endpoint.</summary>
    public string LinkTo(string name, RouteValueDictionary? values = null) => Http.RequestServices.GetRequiredService<LinkGenerator>().GetPathByName(Http, name, values ?? new RouteValueDictionary()) ?? throw new InvalidOperationException($"Unknown route '{name}'.");
    /// <summary>Builds named-route values from statically preserved public properties.</summary>
    public string LinkTo<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TValues>(string name, TValues values)
    {
        var dictionary = new RouteValueDictionary();
        foreach (var property in typeof(TValues).GetProperties())
        {
            dictionary[property.Name] = property.GetValue(values);
        }

        return LinkTo(name, dictionary);
    }
}

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
public sealed record HttpResponseDescription(Type SourceType, string? Variant, IReadOnlyList<int> StatusCodes, Type? BodyType, string? ContentType, IReadOnlyDictionary<string, Type> Headers, IReadOnlyDictionary<string, Type> Extensions);

/// <summary>The final HTTP contract used by both execution and OpenAPI.</summary>
public sealed record HttpEndpointBinding(ServiceOperationDescriptor Operation, IReadOnlyList<HttpInputDescription> Inputs, IReadOnlyList<HttpResponseDescription> Responses, IReadOnlyList<Type> FailureCases);

/// <summary>Describes an externally supplied opaque input or writer without probing delegates.</summary>
public sealed record HttpBodyDescription(Type BodyType, string ContentType = "application/json", bool Required = true);
