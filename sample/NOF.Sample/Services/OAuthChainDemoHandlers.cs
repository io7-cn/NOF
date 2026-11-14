using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using NOF.Hosting;
using NOF.Sample.UI.Services;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace NOF.Sample.Services;

public static class OAuthChainDemoContextKeys
{
    public static readonly object AccessToken = new();
}

public sealed class CreateClientHandler(OAuthChainDemoBackend backend) : OAuthChainDemoService.CreateClient
{
    public override async Task<Result> HandleAsync(
        CreateDemoOAuthClientRequest request,
        InvocationContext context,
        CancellationToken cancellationToken)
    {
        _ = context;
        return (await backend.CreateClientAsync(request, cancellationToken)).Value switch
        {
            CreateDemoOAuthClientResponse value => value,
            HttpTransportFailure { StatusCode: HttpStatusCode.Unauthorized } failure => new Unauthorized(failure.Detail),
            HttpTransportFailure { StatusCode: HttpStatusCode.Forbidden } failure => new Forbidden(failure.Detail),
            HttpTransportFailure failure when (int)failure.StatusCode >= 500 => new ServerError(failure.Detail),
            HttpTransportFailure failure => new Rejected(failure.Detail),
            _ => throw new InvalidOperationException("No OAuth response.")
        };
    }
}

public sealed class GetClientTokenHandler(OAuthChainDemoBackend backend) : OAuthChainDemoService.GetClientToken
{
    public override async Task<Result> HandleAsync(
        GetDemoClientTokenRequest request,
        InvocationContext context,
        CancellationToken cancellationToken)
    {
        _ = context;
        return (await backend.GetClientTokenAsync(request, cancellationToken)).Value switch
        {
            DemoTokenResponse value => value,
            HttpTransportFailure { StatusCode: HttpStatusCode.Unauthorized } failure => new Unauthorized(failure.Detail),
            HttpTransportFailure { StatusCode: HttpStatusCode.Forbidden } failure => new Forbidden(failure.Detail),
            HttpTransportFailure failure when (int)failure.StatusCode >= 500 => new ServerError(failure.Detail),
            HttpTransportFailure failure => new Rejected(failure.Detail),
            _ => throw new InvalidOperationException("No OAuth response.")
        };
    }
}

public sealed class GetUserTokenHandler(OAuthChainDemoBackend backend) : OAuthChainDemoService.GetUserToken
{
    public override async Task<Result> HandleAsync(
        GetDemoUserTokenRequest request,
        InvocationContext context,
        CancellationToken cancellationToken)
    {
        _ = request;
        _ = context;
        return (await backend.GetUserTokenAsync(request, cancellationToken)).Value switch
        {
            DemoTokenResponse value => value,
            HttpTransportFailure { StatusCode: HttpStatusCode.Unauthorized } failure => new Unauthorized(failure.Detail),
            HttpTransportFailure { StatusCode: HttpStatusCode.Forbidden } failure => new Forbidden(failure.Detail),
            HttpTransportFailure failure when (int)failure.StatusCode >= 500 => new ServerError(failure.Detail),
            HttpTransportFailure failure => new Rejected(failure.Detail),
            _ => throw new InvalidOperationException("No OAuth response.")
        };
    }
}

public sealed class ExchangeTokenHandler(OAuthChainDemoBackend backend) : OAuthChainDemoService.ExchangeToken
{
    public override async Task<Result> HandleAsync(
        ExchangeDemoTokenRequest request,
        InvocationContext context,
        CancellationToken cancellationToken)
    {
        _ = context;
        return (await backend.ExchangeTokenAsync(request, cancellationToken)).Value switch
        {
            DemoTokenResponse value => value,
            HttpTransportFailure { StatusCode: HttpStatusCode.Unauthorized } failure => new Unauthorized(failure.Detail),
            HttpTransportFailure { StatusCode: HttpStatusCode.Forbidden } failure => new Forbidden(failure.Detail),
            HttpTransportFailure failure when (int)failure.StatusCode >= 500 => new ServerError(failure.Detail),
            HttpTransportFailure failure => new Rejected(failure.Detail),
            _ => throw new InvalidOperationException("No OAuth response.")
        };
    }
}

public sealed class CallDownstreamHandler(IDemoDownstreamApi downstreamClient) : OAuthChainDemoService.CallDownstream
{
    public override async Task<Result> HandleAsync(
        CallDemoDownstreamRequest request,
        InvocationContext context,
        CancellationToken cancellationToken)
    {
        _ = context;
        var downstreamContext = Context.Empty.WithItem(OAuthChainDemoContextKeys.AccessToken, request.AccessToken);
        return (await downstreamClient.InspectAccessTokenAsync(Empty.Instance, downstreamContext, cancellationToken))
            .Match<Result>(value => value, rejected => new Rejected(rejected.Detail),
                unauthorized => new Unauthorized(unauthorized.Detail), forbidden => new Forbidden(forbidden.Detail),
                serverError => new ServerError(serverError.Detail));
    }
}

public sealed class InspectAccessTokenHandler(IUserContext userContext) : DemoDownstreamService.InspectAccessToken
{
    public override Task<Result> HandleAsync(
        Empty request,
        InvocationContext context,
        CancellationToken cancellationToken)
    {
        _ = request;
        _ = context;
        _ = cancellationToken;

        if (!userContext.User.IsAuthenticated)
        {
            return Task.FromResult<Result>(new Unauthorized("Access token is required."));
        }

        var scopes = userContext.User.FindAll("scope")
            .SelectMany(static claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        var actor = ResolveActor(userContext.User);

        return Task.FromResult<Result>(new ConsumeDemoAccessTokenResponse
        {
            IsAuthenticated = true,
            Subject = userContext.User.FindFirst("sub")?.Value,
            TenantId = userContext.User.TenantId,
            ActorSubject = actor?.Subject,
            ActorJson = actor?.Json,
            Permissions = userContext.User.Permissions.OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
            Scopes = scopes
        });
    }

    private static (string? Subject, string Json)? ResolveActor(ClaimsPrincipal user)
    {
        var actorJson = user.FindFirst(Claims.Actor)?.Value;
        if (string.IsNullOrWhiteSpace(actorJson))
        {
            return null;
        }

        using var document = JsonDocument.Parse(actorJson);
        var root = document.RootElement;
        var subject = root.TryGetProperty(Claims.Subject, out var subjectElement)
            ? subjectElement.GetString()
            : null;

        return string.IsNullOrWhiteSpace(subject)
            ? null
            : (subject, actorJson);
    }
}

/// <summary>Adds the demo's per-call access token to its downstream HTTP request.</summary>
public sealed class OAuthChainDemoAccessTokenHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization is null
            && request.Options.TryGetValue(NOF.Sample.UI.Services.SampleHttpApi.ContextKey, out var context)
            && context.TryGetItem(OAuthChainDemoContextKeys.AccessToken, out var tokenObject)
            && tokenObject is string token && !string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
        return base.SendAsync(request, cancellationToken);
    }
}
