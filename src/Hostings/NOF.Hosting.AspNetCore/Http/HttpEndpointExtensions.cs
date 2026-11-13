using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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
    public HttpOperationBuilder(IEndpointConventionBuilder nativeBuilder)
    {
        NativeBuilder = nativeBuilder;
    }

    /// <summary>Gets the native convention builder for native-specific extensions.</summary>
    public IEndpointConventionBuilder NativeBuilder { get; }

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

    /// <summary>Configures one typed source mapping.</summary>
    public HttpOperationBuilder<TRequest> Response<T>(Action<HttpResponseBuilder<T>> configure)
    {
        HttpEndpointExtensions.AddResponse(this, typeof(T), null, null, configure);
        return this;
    }

    /// <summary>Configures a named source variant.</summary>
    public HttpOperationBuilder<TRequest> Response<T>(string name, Action<HttpResponseBuilder<T>> configure)
    {
        HttpEndpointExtensions.AddResponse(this, typeof(T), name, null, configure);
        return this;
    }

    /// <summary>Configures HTTP defaults for an invocation filter without enabling it.</summary>
    public HttpFilterView<HttpOperationBuilder<TRequest>> Filter<TFilter>()
        where TFilter : IInvocationFilter => new(this, typeof(TFilter), TFilter.FailureCases);
    /// <summary>Configures an explicit nullable response.</summary>
    public HttpOperationBuilder<TRequest> ResponseNull(Action<HttpResponseBuilder<object?>> configure)
    {
        HttpEndpointExtensions.AddResponse(this, typeof(void), null, null, configure);
        return this;
    }

    /// <summary>Selects a final named mapping; the selector does not define its schema.</summary>
    public HttpOperationBuilder<TRequest> SelectResponse(Func<object?, HttpResponseMappings, HttpResponseReference> selector)
    {
        Add(endpoint => endpoint.Metadata.Add(new HttpResponseSelector(selector)));
        return this;
    }
}

/// <summary>An optional selector replacing only the selector slot.</summary>
public sealed record HttpResponseSelector(Func<object?, HttpResponseMappings, HttpResponseReference> Select);

/// <summary>A filter-owned view for HTTP defaults. This never enables an invocation filter.</summary>
public sealed class HttpFilterView<TBuilder>(TBuilder builder, Type filterType, IReadOnlyList<InvocationFailureCase> cases)
    where TBuilder : IEndpointConventionBuilder
{
    /// <summary>Configures one declared filter failure.</summary>
    public HttpFilterView<TBuilder> Response<T>(Action<HttpResponseBuilder<T>> configure)
    {
        if (!cases.Any(item => item.CaseType == typeof(T)))
        {
            throw new ArgumentException("The failure is outside this filter's directory.");
        }

        HttpEndpointExtensions.AddResponse(builder, typeof(T), null, filterType, configure);
        return this;
    }

    /// <summary>Configures common Problem fields for this filter's failures.</summary>
    public HttpFilterView<TBuilder> ConfigureProblem(Action<HttpProblemBuilder<IFailure>> configure)
    {
        HttpEndpointExtensions.AddResponse<IFailure>(builder, typeof(IFailure), null, filterType, output => output.ConfigureProblem(configure));
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
                foreach (var status in response.StatusCodes)
                {
                    endpoint.Metadata.Add(new ProducesResponseTypeMetadata(status, response.BodyType, response.ContentType is null ? [] : [response.ContentType]));
                }
            }
        });
        return new(native);
    }

    /// <summary>Native API explorer marker; execution uses the separately compiled binding.</summary>
    public static void DescribeEndpoint()
    {
    }

    /// <summary>Configures an independently inherited response mapping.</summary>
    public static RouteGroupBuilder Response<T>(this RouteGroupBuilder builder, Action<HttpResponseBuilder<T>> configure)
    {
        AddResponse(builder, typeof(T), null, null, configure);
        return builder;
    }

    /// <summary>Configures common failure fields.</summary>
    public static TBuilder ConfigureProblem<TBuilder>(this TBuilder builder, Action<HttpProblemBuilder<IFailure>> configure)
        where TBuilder : IEndpointConventionBuilder
    {
        AddResponse<IFailure>(builder, typeof(IFailure), null, null, output => output.ConfigureProblem(configure));
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

    /// <summary>Adds one final response rule.</summary>
    public static void AddResponse<T>(IEndpointConventionBuilder builder, Type source, string? variant, Type? filter, Action<HttpResponseBuilder<T>> configure)
    {
        var rule = new HttpResponseRule
        {
            SourceType = source,
            Variant = variant,
            FilterType = filter,
            Endpoint = builder is not RouteGroupBuilder
        };
        configure(new(rule));
        builder.Add(endpoint => endpoint.Metadata.Add(rule));
    }

    /// <summary>Creates a filter-default consumer view on a native group.</summary>
    public static HttpFilterView<RouteGroupBuilder> Filter<TFilter>(this RouteGroupBuilder builder)
        where TFilter : IInvocationFilter => new(builder, typeof(TFilter), TFilter.FailureCases);
    /// <summary>Creates a filter-default consumer view on a typed operation endpoint.</summary>
    public static HttpFilterView<HttpOperationBuilder<TRequest>> Filter<TFilter, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)]
    TRequest>(this HttpOperationBuilder<TRequest> builder)
        where TFilter : IInvocationFilter => new(builder, typeof(TFilter), TFilter.FailureCases);
}

/// <summary>Bridges static operation type arguments into the typed HTTP request binder.</summary>
public sealed class HttpOperationMappingVisitor(IEndpointRouteBuilder endpoints, string pattern, IEnumerable<string> methods) : IServiceOperationDescriptorVisitor<IEndpointConventionBuilder>
{
    /// <inheritdoc/>
    public IEndpointConventionBuilder Visit<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest, TResponse>(ServiceOperationDescriptor<TRequest, TResponse> descriptor) => endpoints.MapOperation(pattern, methods, descriptor).NativeBuilder;
}
