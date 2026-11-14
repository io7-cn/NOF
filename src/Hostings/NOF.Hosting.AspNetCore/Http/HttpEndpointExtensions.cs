using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
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

/// <summary>Native route/endpoint conventions for typed application operations.</summary>
public static class HttpEndpointExtensions
{
    /// <summary>Maps explicit HTTP methods to a statically constrained operation type.</summary>
    public static HttpOperationEndpointBuilder<TOperation> MapOperation<TOperation>(this IEndpointRouteBuilder endpoints, string pattern, IEnumerable<string> methods)
        where TOperation : ServiceOperation, IServiceOperationDefinition => new(TOperation.Describe(new HttpOperationMappingVisitor(endpoints, pattern, methods)));
    /// <summary>Maps GET to a statically constrained operation type.</summary>
    public static HttpOperationEndpointBuilder<TOperation> MapGet<TOperation>(this IEndpointRouteBuilder endpoints, string pattern)
        where TOperation : ServiceOperation, IServiceOperationDefinition => new(TOperation.Describe(new HttpOperationMappingVisitor(endpoints, pattern, ["GET"])));
    /// <summary>Maps POST to a statically constrained operation type.</summary>
    public static HttpOperationEndpointBuilder<TOperation> MapPost<TOperation>(this IEndpointRouteBuilder endpoints, string pattern)
        where TOperation : ServiceOperation, IServiceOperationDefinition => new(TOperation.Describe(new HttpOperationMappingVisitor(endpoints, pattern, ["POST"])));
    /// <summary>Maps PUT to a statically constrained operation type.</summary>
    public static HttpOperationEndpointBuilder<TOperation> MapPut<TOperation>(this IEndpointRouteBuilder endpoints, string pattern)
        where TOperation : ServiceOperation, IServiceOperationDefinition => new(TOperation.Describe(new HttpOperationMappingVisitor(endpoints, pattern, ["PUT"])));
    /// <summary>Maps PATCH to a statically constrained operation type.</summary>
    public static HttpOperationEndpointBuilder<TOperation> MapPatch<TOperation>(this IEndpointRouteBuilder endpoints, string pattern)
        where TOperation : ServiceOperation, IServiceOperationDefinition => new(TOperation.Describe(new HttpOperationMappingVisitor(endpoints, pattern, ["PATCH"])));
    /// <summary>Maps DELETE to a statically constrained operation type.</summary>
    public static HttpOperationEndpointBuilder<TOperation> MapDelete<TOperation>(this IEndpointRouteBuilder endpoints, string pattern)
        where TOperation : ServiceOperation, IServiceOperationDefinition => new(TOperation.Describe(new HttpOperationMappingVisitor(endpoints, pattern, ["DELETE"])));
    /// <summary>Registers an HTTP GET using an application descriptor.</summary>
    public static HttpOperationBuilder<TRequest> MapGet<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest, TResponse>(this IEndpointRouteBuilder endpoints, string pattern, ServiceOperationDescriptor<TRequest, TResponse> operation) => MapOperation(endpoints, pattern, ["GET"], operation);
    /// <summary>Registers an HTTP POST using an application descriptor.</summary>
    public static HttpOperationBuilder<TRequest> MapPost<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest, TResponse>(this IEndpointRouteBuilder endpoints, string pattern, ServiceOperationDescriptor<TRequest, TResponse> operation) => MapOperation(endpoints, pattern, ["POST"], operation);
    /// <summary>Registers an HTTP PUT using an application descriptor.</summary>
    public static HttpOperationBuilder<TRequest> MapPut<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest, TResponse>(this IEndpointRouteBuilder endpoints, string pattern, ServiceOperationDescriptor<TRequest, TResponse> operation) => MapOperation(endpoints, pattern, ["PUT"], operation);
    /// <summary>Registers an HTTP PATCH using an application descriptor.</summary>
    public static HttpOperationBuilder<TRequest> MapPatch<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest, TResponse>(this IEndpointRouteBuilder endpoints, string pattern, ServiceOperationDescriptor<TRequest, TResponse> operation) => MapOperation(endpoints, pattern, ["PATCH"], operation);
    /// <summary>Registers an HTTP DELETE using an application descriptor.</summary>
    public static HttpOperationBuilder<TRequest> MapDelete<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest, TResponse>(this IEndpointRouteBuilder endpoints, string pattern, ServiceOperationDescriptor<TRequest, TResponse> operation) => MapOperation(endpoints, pattern, ["DELETE"], operation);
    /// <summary>Registers explicit methods without adding services to a built container.</summary>
    public static HttpOperationBuilder<TRequest> MapOperation<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest, TResponse>(this IEndpointRouteBuilder endpoints, string pattern, IEnumerable<string> methods, ServiceOperationDescriptor<TRequest, TResponse> operation)
    {
        HttpEndpointExecutor<TRequest>? binding = null;
        var methodList = methods.ToArray();
        RequestDelegate execute = http => (binding ?? throw new InvalidOperationException("HTTP binding is not built.")).ExecuteAsync(http);
        var native = endpoints.MapMethods(pattern, methodList, execute);
        native.Add(endpoint =>
        {
            endpoint.Metadata.Add(typeof(HttpEndpointExtensions).GetMethod(nameof(DescribeEndpoint))!);
            endpoint.Metadata.Add(operation);
        });
        native.Finally(endpoint =>
        {
            binding = HttpEndpointExecutor<TRequest>.Compile(endpoint, operation, methodList);
            endpoint.Metadata.Add(binding.Binding);
            foreach (var response in binding.Binding.Responses)
            {
                endpoint.Metadata.Add(new ProducesResponseTypeMetadata(response.StatusCode, response.BodyType, response.ContentType is null ? [] : [response.ContentType]));
            }
        });
        return new(new RouteHandlerBuilder([native]));
    }

    /// <summary>Native API explorer marker; execution uses the separately compiled binding.</summary>
    public static void DescribeEndpoint()
    {
    }

    /// <summary>Provides a complete fixed-status mapping for descendant endpoints.</summary>
    public static RouteGroupBuilder Response<T>(this RouteGroupBuilder builder, int statusCode)
    {
        ((IEndpointConventionBuilder)builder).Add(endpoint => endpoint.Metadata.Add(HttpResponseMapping.Status<T>(statusCode, false)));
        return builder;
    }
    /// <summary>Provides a complete typed native result for descendant endpoints.</summary>
    public static RouteGroupBuilder Response<T, TResult>(this RouteGroupBuilder builder, Func<T, TResult> present)
        where TResult : IResult, IEndpointMetadataProvider => builder.Response<T, TResult>((value, _) => present(value));
    /// <summary>Provides a boundary-aware native result for descendant endpoints.</summary>
    public static RouteGroupBuilder Response<T, TResult>(this RouteGroupBuilder builder, Func<T, HttpContext, TResult> present)
        where TResult : IResult, IEndpointMetadataProvider
    {
        ((IEndpointConventionBuilder)builder).Add(endpoint => endpoint.Metadata.Add(HttpResponseMapping.Native(present, false)));
        return builder;
    }

    /// <summary>Configures inherited explicit context inputs.</summary>
    public static TBuilder Context<TBuilder>(this TBuilder builder, Action<HttpContextBuilder> configure)
        where TBuilder : IEndpointConventionBuilder
    {
        var value = new HttpContextBuilder();
        configure(value);
        builder.Add(endpoint => endpoint.Metadata.Add(value));
        return builder;
    }

    /// <summary>Configures inherited name matching options.</summary>
    public static TBuilder Requests<TBuilder>(this TBuilder builder, Action<HttpRequestOptions> configure)
        where TBuilder : IEndpointConventionBuilder
    {
        var value = new HttpRequestOptions();
        configure(value);
        builder.Add(endpoint => endpoint.Metadata.Add(value));
        return builder;
    }

}

/// <summary>Bridges static operation type arguments into the typed HTTP request binder.</summary>
public sealed class HttpOperationMappingVisitor(IEndpointRouteBuilder endpoints, string pattern, IEnumerable<string> methods) : IServiceOperationDescriptorVisitor<RouteHandlerBuilder>
{
    /// <inheritdoc/>
    public RouteHandlerBuilder Visit<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest, TResponse>(ServiceOperationDescriptor<TRequest, TResponse> descriptor) => endpoints.MapOperation(pattern, methods, descriptor).NativeBuilder;
}
