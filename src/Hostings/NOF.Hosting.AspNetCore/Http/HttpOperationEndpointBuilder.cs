using Microsoft.AspNetCore.Builder;
using NOF.Application;
using NOF.Infrastructure;

namespace NOF.Hosting.AspNetCore;

/// <summary>A native endpoint selected by its generated operation type.</summary>
public sealed class HttpOperationEndpointBuilder<TOperation>(IEndpointConventionBuilder nativeBuilder) : IEndpointConventionBuilder where TOperation : ServiceOperation, IServiceOperationDefinition
{
    /// <summary>Gets the underlying native builder for native-specific conventions.</summary>
    public IEndpointConventionBuilder NativeBuilder { get; } = nativeBuilder;

    /// <inheritdoc/>
    public void Add(Action<EndpointBuilder> convention) => NativeBuilder.Add(convention);
    /// <inheritdoc/>
    public void Finally(Action<EndpointBuilder> convention) => NativeBuilder.Finally(convention);
    /// <summary>Configures a concrete source case.</summary>
    public HttpOperationEndpointBuilder<TOperation> Response<T>(Action<HttpResponseBuilder<T>> configure)
    {
        HttpEndpointExtensions.AddResponse(this, typeof(T), null, null, configure);
        return this;
    }

    /// <summary>Configures a named source variant.</summary>
    public HttpOperationEndpointBuilder<TOperation> Response<T>(string name, Action<HttpResponseBuilder<T>> configure)
    {
        HttpEndpointExtensions.AddResponse(this, typeof(T), name, null, configure);
        return this;
    }

    /// <summary>Configures an explicitly nullable result.</summary>
    public HttpOperationEndpointBuilder<TOperation> ResponseNull(Action<HttpResponseBuilder<object?>> configure)
    {
        HttpEndpointExtensions.AddResponse(this, typeof(void), null, null, configure);
        return this;
    }

    /// <summary>Configures defaults for a filter's declared failure directory.</summary>
    public HttpFilterView<HttpOperationEndpointBuilder<TOperation>> Filter<TFilter>()
        where TFilter : IInvocationFilter => new(this, typeof(TFilter), TFilter.FailureCases);
    /// <summary>Selects one of the final declared response variants.</summary>
    public HttpOperationEndpointBuilder<TOperation> SelectResponse(Func<object?, HttpResponseMappings, HttpResponseReference> selector)
    {
        Add(endpoint => endpoint.Metadata.Add(new HttpResponseSelector(selector)));
        return this;
    }
}
