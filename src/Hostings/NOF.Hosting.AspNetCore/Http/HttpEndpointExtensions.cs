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

/// <summary>Native route/endpoint conventions for typed application operations.</summary>
public static class HttpEndpointExtensions
{
    /// <summary>Maps explicit HTTP methods to a statically constrained operation type.</summary>
    public static HttpOperationEndpointBuilder<TOperation> MapOperation<TOperation>(this IEndpointRouteBuilder endpoints, string pattern, IEnumerable<string> methods)
        where TOperation : OperationHandler => new(endpoints.ServiceProvider.GetRequiredService<ApplicationServiceRegistry>().GetOperation(typeof(TOperation)).Accept(new HttpOperationMappingVisitor(endpoints, pattern, methods)));
    /// <summary>Maps GET to a statically constrained operation type.</summary>
    public static HttpOperationEndpointBuilder<TOperation> MapGet<TOperation>(this IEndpointRouteBuilder endpoints, string pattern)
        where TOperation : OperationHandler => new(endpoints.ServiceProvider.GetRequiredService<ApplicationServiceRegistry>().GetOperation(typeof(TOperation)).Accept(new HttpOperationMappingVisitor(endpoints, pattern, ["GET"])));
    /// <summary>Maps POST to a statically constrained operation type.</summary>
    public static HttpOperationEndpointBuilder<TOperation> MapPost<TOperation>(this IEndpointRouteBuilder endpoints, string pattern)
        where TOperation : OperationHandler => new(endpoints.ServiceProvider.GetRequiredService<ApplicationServiceRegistry>().GetOperation(typeof(TOperation)).Accept(new HttpOperationMappingVisitor(endpoints, pattern, ["POST"])));
    /// <summary>Maps PUT to a statically constrained operation type.</summary>
    public static HttpOperationEndpointBuilder<TOperation> MapPut<TOperation>(this IEndpointRouteBuilder endpoints, string pattern)
        where TOperation : OperationHandler => new(endpoints.ServiceProvider.GetRequiredService<ApplicationServiceRegistry>().GetOperation(typeof(TOperation)).Accept(new HttpOperationMappingVisitor(endpoints, pattern, ["PUT"])));
    /// <summary>Maps PATCH to a statically constrained operation type.</summary>
    public static HttpOperationEndpointBuilder<TOperation> MapPatch<TOperation>(this IEndpointRouteBuilder endpoints, string pattern)
        where TOperation : OperationHandler => new(endpoints.ServiceProvider.GetRequiredService<ApplicationServiceRegistry>().GetOperation(typeof(TOperation)).Accept(new HttpOperationMappingVisitor(endpoints, pattern, ["PATCH"])));
    /// <summary>Maps DELETE to a statically constrained operation type.</summary>
    public static HttpOperationEndpointBuilder<TOperation> MapDelete<TOperation>(this IEndpointRouteBuilder endpoints, string pattern)
        where TOperation : OperationHandler => new(endpoints.ServiceProvider.GetRequiredService<ApplicationServiceRegistry>().GetOperation(typeof(TOperation)).Accept(new HttpOperationMappingVisitor(endpoints, pattern, ["DELETE"])));
    /// <summary>Registers an HTTP GET using an application descriptor.</summary>
    public static HttpOperationBuilder<TRequest> MapGet<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest, TResponse>(this IEndpointRouteBuilder endpoints, string pattern, OperationDescriptor<TRequest, TResponse> operation) => MapOperation(endpoints, pattern, ["GET"], operation);
    /// <summary>Registers an HTTP POST using an application descriptor.</summary>
    public static HttpOperationBuilder<TRequest> MapPost<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest, TResponse>(this IEndpointRouteBuilder endpoints, string pattern, OperationDescriptor<TRequest, TResponse> operation) => MapOperation(endpoints, pattern, ["POST"], operation);
    /// <summary>Registers an HTTP PUT using an application descriptor.</summary>
    public static HttpOperationBuilder<TRequest> MapPut<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest, TResponse>(this IEndpointRouteBuilder endpoints, string pattern, OperationDescriptor<TRequest, TResponse> operation) => MapOperation(endpoints, pattern, ["PUT"], operation);
    /// <summary>Registers an HTTP PATCH using an application descriptor.</summary>
    public static HttpOperationBuilder<TRequest> MapPatch<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest, TResponse>(this IEndpointRouteBuilder endpoints, string pattern, OperationDescriptor<TRequest, TResponse> operation) => MapOperation(endpoints, pattern, ["PATCH"], operation);
    /// <summary>Registers an HTTP DELETE using an application descriptor.</summary>
    public static HttpOperationBuilder<TRequest> MapDelete<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest, TResponse>(this IEndpointRouteBuilder endpoints, string pattern, OperationDescriptor<TRequest, TResponse> operation) => MapOperation(endpoints, pattern, ["DELETE"], operation);
    /// <summary>Registers explicit methods without adding services to a built container.</summary>
    public static HttpOperationBuilder<TRequest> MapOperation<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest, TResponse>(this IEndpointRouteBuilder endpoints, string pattern, IEnumerable<string> methods, OperationDescriptor<TRequest, TResponse> operation)
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
