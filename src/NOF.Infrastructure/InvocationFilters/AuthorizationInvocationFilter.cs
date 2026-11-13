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
public sealed class AuthorizationInvocationFilter(
    IUserContext userContext,
    IInboundAuthorizationHandler authorizationHandler,
    IOptions<TenantAuthorizationOptions> tenantOptions) :
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
    public async ValueTask InvokeAsync(InvocationContext context, object input, InvocationDelegate next, CancellationToken cancellationToken)
    {
        var result = await authorizationHandler.AuthorizeAsync(new InboundAuthorizationContext
        {
            User = userContext.User,
            ExecutionContext = context.Context,
            Input = input,
            HandlerType = context.HandlerType,
            HandlerMethodInfo = context.HandlerMethodInfo,
            ServiceType = context.ServiceType,
            ServiceMethodInfo = context.ServiceMethodInfo,
            InputType = context.InputType
        }, cancellationToken).ConfigureAwait(false);
        var denial = GetDenial(result) ?? ValidateRequestTenant(context);
        if (denial is not null)
        {
            context.Reject(denial.Result, denial.ChallengePermissions, denial.Reason);
            return;
        }
        await next(WithTrustedTenant(context), input, cancellationToken);
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
        var isHostOperation = UseHostTenantAttribute.IsRequired(context.ServiceMethodInfo);
        var isAuthenticated = userContext.User.IsAuthenticated;
        var trustedTenant = isAuthenticated ? userContext.User.TenantId : null;
        var normalizedTenant = TenantId.Normalize(trustedTenant);
        if (_tenantOptions.RequireAuthenticatedTenant)
        {
            if (!isAuthenticated)
            {
                return new(new global::NOF.Abstraction.Failure("401", "Please login first"), RejectionReason.Authentication, []);
            }

            if (!isHostOperation && (string.IsNullOrWhiteSpace(trustedTenant)
                || normalizedTenant == NOFAbstractionConstants.Tenant.HostId))
            {
                return new(new global::NOF.Abstraction.Failure("403", "An authenticated tenant is required"), RejectionReason.Authorization, []);
            }
        }

        if (_tenantOptions.RejectMismatchedTenant && !isHostOperation
            && !string.IsNullOrWhiteSpace(trustedTenant)
            && context.TenantId != NOFAbstractionConstants.Tenant.HostId
            && !string.Equals(context.TenantId, normalizedTenant, StringComparison.Ordinal))
        {
            return new(new global::NOF.Abstraction.Failure("403", "Request tenant does not match the authenticated tenant"), RejectionReason.Authorization, []);
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
