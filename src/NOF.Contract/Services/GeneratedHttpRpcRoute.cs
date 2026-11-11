using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace NOF.Contract;

/// <summary>Identifies a generated Controller RPC route in a contract assembly.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public abstract class GeneratedHttpRpcRouteAttribute : Attribute
{
    /// <summary>Gets the generated route.</summary>
    public abstract GeneratedHttpRpcRoute Route { get; }
}

/// <summary>Identifies a generated Controller RPC route without runtime type activation.</summary>
public sealed class GeneratedHttpRpcRouteAttribute<TRoute> : GeneratedHttpRpcRouteAttribute
    where TRoute : GeneratedHttpRpcRoute, new()
{
    /// <inheritdoc />
    public override GeneratedHttpRpcRoute Route { get; } = new TRoute();
}

/// <summary>Describes a parameter bound by a generated Controller RPC route.</summary>
/// <param name="Name">The parameter name in the HTTP request.</param>
/// <param name="Type">The CLR type used to describe the parameter's wire schema.</param>
/// <param name="IsPath">Whether the parameter comes from the path.</param>
/// <param name="IsRequired">Whether the parameter is required.</param>
public sealed record HttpRpcRouteParameter(string Name, Type Type, bool IsPath, bool IsRequired);

/// <summary>Provides the HTTP transport shape and serialization for one Controller RPC operation.</summary>
public abstract class GeneratedHttpRpcRoute
{
    /// <summary>Gets the service that declares the operation.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
    public abstract Type ServiceType { get; }

    /// <summary>Gets the operation name used for dispatch.</summary>
    public abstract string OperationName { get; }

    /// <summary>Gets the complete route template.</summary>
    public abstract string Template { get; }

    /// <summary>Gets the HTTP method.</summary>
    public abstract HttpVerb Verb { get; }

    /// <summary>Gets the path and query parameters.</summary>
    public abstract IReadOnlyList<HttpRpcRouteParameter> Parameters { get; }

    /// <summary>Gets whether the route takes a JSON body.</summary>
    public abstract bool HasBody { get; }

    /// <summary>Gets the declared result type.</summary>
    public abstract Type ResponseType { get; }

    /// <summary>Gets source-generated JSON metadata for the body.</summary>
    public abstract JsonTypeInfo? GetBodyTypeInfo(JsonSerializerOptions options);

    /// <summary>Binds the wire values to one contract request, or returns null for invalid values.</summary>
    public abstract ValueTask<object?> BindAsync(
        Func<string, string?> getPath,
        Func<string, string?> getQuery,
        Func<JsonTypeInfo, CancellationToken, ValueTask<object?>> readBody,
        JsonSerializerOptions options,
        CancellationToken cancellationToken);

    /// <summary>Writes the result using source-generated JSON metadata.</summary>
    public abstract Task WriteResponseAsync(
        Stream body,
        ServiceExecution response,
        JsonSerializerOptions options,
        CancellationToken cancellationToken);
}

/// <summary>Provides strongly typed dispatch and result serialization for generated routes.</summary>
public abstract class GeneratedHttpRpcRoute<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] TService,
    TResponse> : GeneratedHttpRpcRoute
    where TService : class, IApplicationService
{
    /// <inheritdoc />
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
    public override Type ServiceType => typeof(TService);

    /// <inheritdoc />
    public override Type ResponseType => typeof(TResponse);

    /// <summary>Parses a path or query value using the invariant culture.</summary>
    protected static bool TryParse<T>(string? text, [MaybeNullWhen(false)] out T value)
        where T : IParsable<T>
        => T.TryParse(text, CultureInfo.InvariantCulture, out value);

    /// <inheritdoc />
    public override Task WriteResponseAsync(
        Stream body,
        ServiceExecution response,
        JsonSerializerOptions options,
        CancellationToken cancellationToken)
    {
        if (response.Value is ServiceRejected rejected)
        {
            return JsonSerializer.SerializeAsync(body, rejected.Failure, options.GetRequiredTypeInfo<NOF.Abstraction.Failure>(), cancellationToken);
        }
        return JsonSerializer.SerializeAsync(body, response.Require<TResponse>(allowNull: true), options.GetRequiredTypeInfo<TResponse>(), cancellationToken);
    }
}
