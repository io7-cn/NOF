using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using System.Diagnostics.CodeAnalysis;

namespace NOF.Infrastructure;

/// <summary>One immutable Use/Exclude declaration at a specific application scope.</summary>
public sealed record InvocationFilterRule(InvocationFilterScope Scope, Type? ServiceType, string? OperationId,
    Type FilterType, bool Excluded, int? Order, Func<IServiceProvider, IInvocationFilter>? Resolve,
    Action<InvocationDescriptionContext>? Describe, IReadOnlyList<InvocationFailureCase> FailureCases);
