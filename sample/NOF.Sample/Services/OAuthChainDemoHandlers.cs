using NOF.Abstraction;
using NOF.Contract;
using NOF.Hosting;
using NOF.Hosting.AspNetCore.Extension.OidcServer;
using System.Security.Claims;
using System.Text.Json;

namespace NOF.Sample.Services;

public static class OAuthChainDemoContextKeys
{
    public static readonly object AccessToken = new();
}

public sealed class CreateClientHandler(OAuthChainDemoBackend backend) : OAuthChainDemoService.CreateClient
{
    public override async Task<Result> HandleAsync(
        CreateDemoOAuthClientRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        _ = context;
        return (await backend.CreateClientAsync(request, cancellationToken)).Value switch
        {
            CreateDemoOAuthClientResponse value => value,
            Failure { Code: "401" } failure => new Unauthorized(failure.Message),
            Failure { Code: "403" } failure => new Forbidden(failure.Message),
            Failure { Code: "500" } failure => new ServerError(failure.Message),
            Failure failure => new Rejected(failure.Message),
            _ => throw new InvalidOperationException("No OAuth response.")
        };
    }
}

public sealed class GetClientTokenHandler(OAuthChainDemoBackend backend) : OAuthChainDemoService.GetClientToken
{
    public override async Task<Result> HandleAsync(
        GetDemoClientTokenRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        _ = context;
        return (await backend.GetClientTokenAsync(request, cancellationToken)).Value switch
        {
            DemoTokenResponse value => value,
            Failure { Code: "401" } failure => new Unauthorized(failure.Message),
            Failure { Code: "403" } failure => new Forbidden(failure.Message),
            Failure { Code: "500" } failure => new ServerError(failure.Message),
            Failure failure => new Rejected(failure.Message),
            _ => throw new InvalidOperationException("No OAuth response.")
        };
    }
}

public sealed class GetUserTokenHandler(OAuthChainDemoBackend backend) : OAuthChainDemoService.GetUserToken
{
    public override async Task<Result> HandleAsync(
        GetDemoUserTokenRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        _ = request;
        _ = context;
        return (await backend.GetUserTokenAsync(cancellationToken)).Value switch
        {
            DemoTokenResponse value => value,
            Failure { Code: "401" } failure => new Unauthorized(failure.Message),
            Failure { Code: "403" } failure => new Forbidden(failure.Message),
            Failure { Code: "500" } failure => new ServerError(failure.Message),
            Failure failure => new Rejected(failure.Message),
            _ => throw new InvalidOperationException("No OAuth response.")
        };
    }
}

public sealed class ExchangeTokenHandler(OAuthChainDemoBackend backend) : OAuthChainDemoService.ExchangeToken
{
    public override async Task<Result> HandleAsync(
        ExchangeDemoTokenRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        _ = context;
        return (await backend.ExchangeTokenAsync(request, cancellationToken)).Value switch
        {
            DemoTokenResponse value => value,
            Failure { Code: "401" } failure => new Unauthorized(failure.Message),
            Failure { Code: "403" } failure => new Forbidden(failure.Message),
            Failure { Code: "500" } failure => new ServerError(failure.Message),
            Failure failure => new Rejected(failure.Message),
            _ => throw new InvalidOperationException("No OAuth response.")
        };
    }
}

public sealed class CallDownstreamHandler(IDemoDownstreamApi downstreamClient) : OAuthChainDemoService.CallDownstream
{
    public override async Task<Result> HandleAsync(
        CallDemoDownstreamRequest request,
        Context context,
        CancellationToken cancellationToken)
    {
        _ = context;
        var downstreamContext = Context.Empty.WithItem(OAuthChainDemoContextKeys.AccessToken, request.AccessToken);
        return (await downstreamClient.InspectAccessTokenAsync(Empty.Instance, downstreamContext, cancellationToken))
            .Match<Result>(value => value, rejected => new Rejected(rejected.Message),
                unauthorized => new Unauthorized(unauthorized.Message), forbidden => new Forbidden(forbidden.Message),
                serverError => new ServerError(serverError.Message));
    }
}

public sealed class InspectAccessTokenHandler(IUserContext userContext) : DemoDownstreamService.InspectAccessToken
{
    public override Task<Result> HandleAsync(
        Empty request,
        Context context,
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
        var actorJson = user.FindFirst(OAuthClaimTypes.Actor)?.Value;
        if (string.IsNullOrWhiteSpace(actorJson))
        {
            return null;
        }

        using var document = JsonDocument.Parse(actorJson);
        var root = document.RootElement;
        var subject = root.TryGetProperty(OAuthClaimTypes.Subject, out var subjectElement)
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
