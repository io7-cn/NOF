using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using System.Reflection;
using System.Security.Claims;

namespace NOF.Infrastructure;

/// <summary>Classifies the authorization provider's denial before the filter returns its typed case.</summary>
public enum AuthorizationFailureReason
{
    /// <summary>Other provider policy.</summary>
    Policy,
    /// <summary>Authentication is required.</summary>
    Authentication,
    /// <summary>Permission or tenant trust is insufficient.</summary>
    Authorization
}
