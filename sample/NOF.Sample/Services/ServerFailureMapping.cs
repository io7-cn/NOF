using NOF.Abstraction;
using NOF.Infrastructure;

namespace NOF.Sample.Services;

/// <summary>Preserves the UI port's failure presentation while local invocation returns ordinary values.</summary>
public static class ServerFailureMapping
{
    public static Failure From(IInvocationFailure failure) => failure switch
    {
        AuthorizationInvocationFilter.AuthenticationRequired value => new(value.ErrorCode, value.Message),
        AuthorizationInvocationFilter.PermissionDenied value => new(value.Error.ErrorCode, value.Error.Message),
        AuthorizationInvocationFilter.PolicyDenied value => new(value.Error.ErrorCode, value.Error.Message),
        _ => throw new InvalidOperationException($"No UI mapping for filter failure '{failure.GetType()}'.")
    };
}
