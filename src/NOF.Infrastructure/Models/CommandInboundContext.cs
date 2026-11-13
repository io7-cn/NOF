using NOF.Application;
using System.Diagnostics.CodeAnalysis;

namespace NOF.Infrastructure;

/// <summary>Metadata for an incoming command delivery.</summary>
public sealed class CommandInboundContext : InvocationContext
{
    /// <inheritdoc />
    public override InvocationKind Kind => InvocationKind.Command;

    /// <summary>Creates a delivery context.</summary>
    public CommandInboundContext() { }

    [SetsRequiredMembers]
    private CommandInboundContext(IReadOnlyDictionary<object, object?> items, CommandInboundContext source) : base(items, source) { }

    /// <inheritdoc />
    protected override NOF.Contract.Context Clone(IReadOnlyDictionary<object, object?> items) => new CommandInboundContext(items, this);
}
