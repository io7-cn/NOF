using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NOF.Abstraction;
using NOF.Application;
using System.Diagnostics.CodeAnalysis;

namespace NOF.Infrastructure;

/// <summary>Static metadata and a typed delivery delegate; inferred type identities resolve only through registered delegates, never dynamic type loading.</summary>
public sealed record OutboxRegistration(Type ClrType, string MessageType,
    Func<IServiceProvider, NOFOutboxMessage, OutboxDeliveryContext, CancellationToken, ValueTask> Deliver);
