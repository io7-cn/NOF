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

/// <summary>Bridges static operation type arguments into the typed HTTP request binder.</summary>
public sealed class HttpOperationMappingVisitor(IEndpointRouteBuilder endpoints, string pattern, IEnumerable<string> methods) : IOperationDescriptorVisitor<RouteHandlerBuilder>
{
    /// <inheritdoc />
    public RouteHandlerBuilder Visit<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest>(OperationDescriptor<TRequest> descriptor)
        => throw new InvalidOperationException("Void operations have no HTTP business reply. Use a native acceptance endpoint and acknowledge durable submission.");

    /// <inheritdoc/>
    public RouteHandlerBuilder Visit<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest, TResponse>(OperationDescriptor<TRequest, TResponse> descriptor) => endpoints.MapOperation(pattern, methods, descriptor).NativeBuilder;
}
