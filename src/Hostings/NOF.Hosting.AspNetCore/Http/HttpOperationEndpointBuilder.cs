using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using NOF.Application;
using NOF.Infrastructure;

namespace NOF.Hosting.AspNetCore;

/// <summary>A native endpoint selected by its generated operation type.</summary>
public sealed class HttpOperationEndpointBuilder<TOperation>(RouteHandlerBuilder nativeBuilder) : IEndpointConventionBuilder where TOperation : ServiceOperation, IServiceOperationDefinition
{
    /// <summary>Gets the underlying native builder for native-specific conventions.</summary>
    public RouteHandlerBuilder NativeBuilder { get; } = nativeBuilder;

    /// <inheritdoc/>
    public void Add(Action<EndpointBuilder> convention) => NativeBuilder.Add(convention);
    /// <inheritdoc/>
    public void Finally(Action<EndpointBuilder> convention) => NativeBuilder.Finally(convention);
    /// <summary>Replaces this result type's mapping with a fixed status and default representation.</summary>
    public HttpOperationEndpointBuilder<TOperation> Response<T>(int statusCode)
    {
        Add(endpoint => endpoint.Metadata.Add(HttpResponseMapping.Status<T>(statusCode, true)));
        return this;
    }
    /// <summary>Replaces this result type's mapping with one complete native result.</summary>
    public HttpOperationEndpointBuilder<TOperation> Response<T, TResult>(Func<T, TResult> present) where TResult : IResult, IEndpointMetadataProvider
        => Response<T, TResult>((value, _) => present(value));
    /// <summary>Replaces this result type's mapping using the native HTTP boundary.</summary>
    public HttpOperationEndpointBuilder<TOperation> Response<T, TResult>(Func<T, HttpContext, TResult> present) where TResult : IResult, IEndpointMetadataProvider
    {
        Add(endpoint => endpoint.Metadata.Add(HttpResponseMapping.Native(present, true)));
        return this;
    }
}
