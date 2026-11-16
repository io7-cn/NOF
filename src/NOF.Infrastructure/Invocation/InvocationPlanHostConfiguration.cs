using NOF.Abstraction;
using NOF.Application;
using System.Collections.Concurrent;

namespace NOF.Infrastructure;

/// <summary>Frozen host configuration used only for description, never request identity.</summary>
public sealed record InvocationPlanHostConfiguration(bool CustomAuthorization,
    bool RequireAuthenticatedTenant, bool RejectMismatchedTenant, bool UsesDefaultEngine);
