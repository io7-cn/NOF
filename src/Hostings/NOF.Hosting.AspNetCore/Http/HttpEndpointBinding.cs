using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using System.Diagnostics.CodeAnalysis;

namespace NOF.Hosting.AspNetCore;

/// <summary>The final HTTP contract used by both execution and OpenAPI.</summary>
public sealed record HttpEndpointBinding(OperationDescriptor Operation, IReadOnlyList<HttpInputDescription> Inputs, IReadOnlyList<HttpResponseDescription> Responses, IReadOnlyList<Type> FailureCases);
