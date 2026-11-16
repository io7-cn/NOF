using Microsoft.Extensions.Options;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using NOF.Hosting;
using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;

namespace NOF.Infrastructure;

/// <summary>Applies shared authorization and adapter-specific tenant rejection.</summary>
[FailureCase("AuthenticationRequired", "Authentication required")]
[FailureCase("PermissionDenied", "Permission denied", typeof(PermissionDeniedDetails))]
[FailureCase("PolicyDenied", "Policy denied", typeof(PolicyDeniedDetails))]
public sealed partial class AuthorizationInvocationFilter(
    IUserContext userContext,
    IInboundAuthorizationHandler authorizationHandler,
    IOptions<TenantAuthorizationOptions> tenantOptions,
    IInvocationPlanProvider? plans = null) :
    IInvocationFilter
{
    private readonly TenantAuthorizationOptions _tenantOptions = tenantOptions.Value;

    /// <summary>Creates an authorization filter with the default tenant trust policy.</summary>
    public AuthorizationInvocationFilter(IUserContext userContext,
        IInboundAuthorizationHandler authorizationHandler)
        : this(userContext, authorizationHandler, Microsoft.Extensions.Options.Options.Create(new TenantAuthorizationOptions()))
    {
    }

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(InvocationContext context, object input, InvocationDelegate next, CancellationToken cancellationToken)
    {
        var denial = ValidateRequestTenant(context);
        var trusted = WithTrustedTenant(context);
        if (denial is null)
        {
            var result = await authorizationHandler.AuthorizeAsync(new InboundAuthorizationContext
            {
                Policy = plans?.Describe(context.Operation, context.Origin).Authorization?.Authorization
                    ?? (context.ServiceType is { } contract
                        ? AuthorizationPolicyResolver.Resolve(contract, context.ServiceMethodInfo)
                        : AuthorizationPolicy.Anonymous),
                User = userContext.User,
                ExecutionContext = trusted.Context,
                Input = input,
                HandlerType = context.HandlerType,
                HandlerMethodInfo = context.HandlerMethodInfo,
                ServiceType = context.ServiceType,
                ServiceMethodInfo = context.ServiceMethodInfo,
                InputType = context.InputType
            }, cancellationToken).ConfigureAwait(false);
            denial = GetDenial(result);
        }
        if (denial is not null)
        {
            return denial.Reason switch
            {
                AuthorizationFailureReason.Authentication => new AuthenticationRequired(denial.Result.Detail),
                AuthorizationFailureReason.Authorization => new PermissionDenied(new(denial.ChallengePermissions), denial.Result.Detail),
                _ => new PolicyDenied(new(denial.Result.Title), denial.Result.Detail)
            };
        }
        return await next(trusted, input, cancellationToken);
    }

    /// <summary>Selects failures from the same declaration policy used by the authorization provider.</summary>
    public static void Describe(InvocationDescriptionContext description)
    {
        var policy = description.GetMetadata<InvocationAuthorizationPolicy>();
        if (policy?.RequiresAuthentication == true)
        {
            description.Include<AuthenticationRequired>();
        }
        if (policy?.RequiresAuthorization == true)
        {
            description.Include<PermissionDenied>();
        }
        if (policy?.CustomAuthorization == true)
        {
            description.Include<PolicyDenied>();
        }
    }

    private static InboundAuthorizationResult.Denied? GetDenial(InboundAuthorizationResult result)
        => result switch
        {
            InboundAuthorizationResult.Allowed => null,
            InboundAuthorizationResult.Denied denied => denied,
            null => throw new InvalidOperationException("The authorization handler returned an uninitialized result.")
        };

    private InboundAuthorizationResult.Denied? ValidateRequestTenant(InvocationContext context)
    {
        var isAuthenticated = userContext.User.IsAuthenticated;
        var trustedTenant = isAuthenticated ? userContext.User.TenantId : null;
        var normalizedTenant = TenantId.Normalize(trustedTenant);
        if (_tenantOptions.RequireAuthenticatedTenant)
        {
            if (!isAuthenticated)
            {
                return new(new Failure("401", "Please login first"), AuthorizationFailureReason.Authentication, []);
            }

            if (string.IsNullOrWhiteSpace(trustedTenant)
                || normalizedTenant == NOFAbstractionConstants.Tenant.HostId)
            {
                return new(new Failure("403", "An authenticated tenant is required"), AuthorizationFailureReason.Authorization, []);
            }
        }

        if (_tenantOptions.RejectMismatchedTenant
            && !string.IsNullOrWhiteSpace(trustedTenant)
            && context.TenantId != NOFAbstractionConstants.Tenant.HostId
            && !string.Equals(context.TenantId, normalizedTenant, StringComparison.Ordinal))
        {
            return new(new Failure("403", "Request tenant does not match the authenticated tenant"), AuthorizationFailureReason.Authorization, []);
        }

        return null;
    }

    private InvocationContext WithTrustedTenant(InvocationContext context)
    {
        if (!userContext.User.IsAuthenticated)
        {
            return context;
        }

        var trustedTenantId = userContext.User.TenantId;
        if (string.IsNullOrWhiteSpace(trustedTenantId))
        {
            return context;
        }

        var tenantId = TenantId.Normalize(trustedTenantId);
        Activity.Current?.SetTag(NOFInfrastructureConstants.InboundPipeline.Tags.TenantId, tenantId);
        return context.WithTenantId(tenantId);
    }
}
