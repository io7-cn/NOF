using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using NOF.Infrastructure;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace NOF.Hosting.AspNetCore;

/// <summary>Typed view over a native endpoint convention builder.</summary>
public sealed class HttpOperationBuilder<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest> : IEndpointConventionBuilder
{
    /// <summary>Creates a typed native endpoint view.</summary>
    public HttpOperationBuilder(RouteHandlerBuilder nativeBuilder)
    {
        NativeBuilder = nativeBuilder;
    }

    /// <summary>Gets the native convention builder for native-specific extensions.</summary>
    public RouteHandlerBuilder NativeBuilder { get; }

    /// <inheritdoc/>
    public void Add(Action<EndpointBuilder> convention) => NativeBuilder.Add(convention);
    /// <inheritdoc/>
    public void Finally(Action<EndpointBuilder> finallyConvention) => NativeBuilder.Finally(finallyConvention);
    /// <summary>Configures this endpoint's typed input.</summary>
    public HttpOperationBuilder<TRequest> Request(Action<HttpRequestBuilder<TRequest>> configure)
    {
        var input = new HttpRequestBuilder<TRequest>();
        configure(input);
        Add(endpoint => endpoint.Metadata.Add(input));
        return this;
    }

    /// <summary>Replaces this result type's mapping with a fixed status and default representation.</summary>
    public HttpOperationBuilder<TRequest> Response<T>(int statusCode)
    {
        Add(endpoint => endpoint.Metadata.Add(HttpResponseMapping.Status<T>(statusCode, true)));
        return this;
    }
    /// <summary>Replaces this result type's mapping with one complete native result.</summary>
    public HttpOperationBuilder<TRequest> Response<T, TResult>(Func<T, TResult> present) where TResult : IResult, IEndpointMetadataProvider
        => Response<T, TResult>((value, _) => present(value));
    /// <summary>Replaces this result type's mapping using the native HTTP boundary.</summary>
    public HttpOperationBuilder<TRequest> Response<T, TResult>(Func<T, HttpContext, TResult> present) where TResult : IResult, IEndpointMetadataProvider
    {
        Add(endpoint => endpoint.Metadata.Add(HttpResponseMapping.Native(present, true)));
        return this;
    }
}
