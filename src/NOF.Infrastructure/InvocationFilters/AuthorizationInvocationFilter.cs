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
    IOptions<AuthenticationResourceServerOptions> options,
    IOptions<TenantAuthorizationOptions> tenantOptions) :
    IInvocationFilter
{
    private const string WwwAuthenticateHeader = "WWW-Authenticate";
    private readonly AuthenticationResourceServerOptions _options = options.Value;
    private readonly TenantAuthorizationOptions _tenantOptions = tenantOptions.Value;

    /// <summary>Creates an authorization filter with the default tenant trust policy.</summary>
    public AuthorizationInvocationFilter(IUserContext userContext,
        IInboundAuthorizationHandler authorizationHandler,
        IOptions<AuthenticationResourceServerOptions> options)
        : this(userContext, authorizationHandler, options, Microsoft.Extensions.Options.Options.Create(new TenantAuthorizationOptions()))
    {
    }

    /// <inheritdoc />
    public async ValueTask InvokeAsync(InvocationContext context, object input, InvocationDelegate next, CancellationToken cancellationToken)
    {
        var service = context as RequestInboundContext;
        var result = await authorizationHandler.AuthorizeAsync(new InboundAuthorizationContext
        {
            Kind = context.Kind,
            User = userContext.User,
            ExecutionContext = context,
            Input = input,
            HandlerType = context.HandlerType,
            HandlerMethodInfo = context.HandlerMethodInfo,
            ServiceType = service?.ServiceType,
            ServiceMethodInfo = service?.ServiceMethodInfo,
            InputType = service is null ? context.InputType : null
        }, cancellationToken).ConfigureAwait(false);
        var denial = GetDenial(result) ?? (service is null ? null : ValidateRequestTenant(service));
        if (denial is not null)
        {
            if (service is not null)
            {
                ApplyFailure(service, denial);
            }
            else
            {
                context.Reject(denial.Result);
            }
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

    private InboundAuthorizationResult.Denied? ValidateRequestTenant(RequestInboundContext context)
    {
        var isHostOperation = UseHostTenantAttribute.IsRequired(context.ServiceMethodInfo);
        var isAuthenticated = userContext.User.IsAuthenticated;
        var trustedTenant = isAuthenticated ? userContext.User.TenantId : null;
        var normalizedTenant = TenantId.Normalize(trustedTenant);
        if (_tenantOptions.RequireAuthenticatedTenantForRequests)
        {
            if (!isAuthenticated)
            {
                return new(new global::NOF.Abstraction.Failure("401", "Please login first"), 401, []);
            }

            if (!isHostOperation && (string.IsNullOrWhiteSpace(trustedTenant)
                || normalizedTenant == NOFAbstractionConstants.Tenant.HostId))
            {
                return new(new global::NOF.Abstraction.Failure("403", "An authenticated tenant is required"), 403, []);
            }
        }

        if (_tenantOptions.RejectMismatchedTenantForRequests && !isHostOperation
            && !string.IsNullOrWhiteSpace(trustedTenant)
            && context.TenantId != NOFAbstractionConstants.Tenant.HostId
            && !string.Equals(context.TenantId, normalizedTenant, StringComparison.Ordinal))
        {
            return new(new global::NOF.Abstraction.Failure("403", "Request tenant does not match the authenticated tenant"), 403, []);
        }

        return null;
    }

    private void ApplyFailure(RequestInboundContext context, InboundAuthorizationResult.Denied failure)
    {
        context.ResponseHeaders[NOFInfrastructureConstants.Transport.Headers.HttpStatusCode] =
            failure.StatusCode.ToString(CultureInfo.InvariantCulture);
        context.ResponseHeaders[WwwAuthenticateHeader] = CreateBearerChallenge(failure);
        context.Reject(failure.Result);
    }

    private string CreateBearerChallenge(InboundAuthorizationResult.Denied failure)
    {
        var parameters = new List<string>
        {
            failure.StatusCode == 403
                ? "error=\"insufficient_scope\""
                : "error=\"invalid_token\""
        };

        if (!string.IsNullOrWhiteSpace(_options.AuthorizationServerIssuer))
        {
            parameters.Add($"authorization_server=\"{EscapeChallengeValue(_options.AuthorizationServerIssuer)}\"");
        }

        if (failure.StatusCode == 403)
        {
            var scope = string.Join(' ', failure.ChallengePermissions.Where(static permission => !string.IsNullOrWhiteSpace(permission)));
            if (!string.IsNullOrWhiteSpace(scope))
            {
                parameters.Add($"scope=\"{EscapeChallengeValue(scope)}\"");
            }
        }

        return $"Bearer {string.Join(", ", parameters)}";
    }

    private static string EscapeChallengeValue(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private TContext WithTrustedTenant<TContext>(TContext context)
        where TContext : Context
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
        return (TContext)context.WithTenantId(tenantId);
    }
}
