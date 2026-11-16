using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using System.Reflection;
using System.Security.Claims;

namespace NOF.Infrastructure;

public sealed class InboundAuthorizationContext
{
    /// <summary>Gets the immutable declaration policy shared with invocation description.</summary>
    public required AuthorizationPolicy Policy { get; init; }
    public required ClaimsPrincipal User { get; init; }

    public required Context ExecutionContext { get; init; }

    public required object Input { get; init; }

    public required Type HandlerType { get; init; }

    public required MethodInfo HandlerMethodInfo { get; init; }

    public Type? ServiceType { get; init; }

    public MethodInfo? ServiceMethodInfo { get; init; }

    public Type? InputType { get; init; }
}
