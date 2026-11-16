using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using NOF.Abstraction;
using NOF.Contract;
using NOF.Infrastructure;
using System.Text.Json;

namespace NOF.Hosting.AspNetCore;
/// <summary>One complete response mapping; a nearer declaration replaces it entirely.</summary>
public sealed record HttpResponseMapping(Type SourceType, int? StatusCode, Func<object?, HttpContext, IResult>? Present, Action<EndpointBuilder>? Describe, bool Endpoint)
{
    /// <summary>Creates the status-only convenience mapping.</summary>
    public static HttpResponseMapping Status<T>(int statusCode, bool endpoint)
    {
        if (statusCode < 200 || statusCode > 599)
        {
            throw new ArgumentOutOfRangeException(nameof(statusCode));
        }

        return new(typeof(T), statusCode, null, null, endpoint);
    }

    /// <summary>Captures a typed native result and its static metadata without running the delegate.</summary>
    public static HttpResponseMapping Native<T, TResult>(Func<T, HttpContext, TResult> present, bool endpoint)
        where TResult : IResult, IEndpointMetadataProvider => new(typeof(T), null, (value, http) => present((T)value!, http), builder => TResult.PopulateMetadata(typeof(HttpEndpointExtensions).GetMethod(nameof(HttpEndpointExtensions.DescribeEndpoint))!, builder), endpoint);
    /// <summary>Describes the complete selected response.</summary>
    public IReadOnlyList<HttpResponseDescription> DescribeResponses(IServiceProvider services)
    {
        if (Describe is not null)
        {
            var temporary = new RouteEndpointBuilder(null, RoutePatternFactory.Parse(""), 0)
            {
                ApplicationServices = services
            };
            Describe(temporary);
            return Array.AsReadOnly(temporary.Metadata.OfType<IProducesResponseTypeMetadata>().Where(item => item.StatusCode >= 200).SelectMany(item => item.ContentTypes.DefaultIfEmpty(null!).Select(content => new HttpResponseDescription(SourceType, item.StatusCode, item.Type == typeof(void) ? null : item.Type, content))).ToArray());
        }

        var status = StatusCode ?? DefaultStatus(SourceType);
        var body = status is 204 or 304 || SourceType == typeof(Empty) ? null : typeof(IFailure).IsAssignableFrom(SourceType) ? typeof(ProblemDetails) : SourceType == typeof(void) ? typeof(object) : SourceType;
        return [new(SourceType, status, body, body is null ? null : typeof(ProblemDetails).IsAssignableFrom(body) ? "application/problem+json" : "application/json")];
    }

    /// <summary>Writes a native result or the standard JSON/Problem representation.</summary>
    public async Task WriteAsync(object? value, HttpContext http, JsonSerializerOptions json)
    {
        if (Present is not null)
        {
            await Present(value, http).ExecuteAsync(http);
            return;
        }

        var status = StatusCode ?? DefaultStatus(SourceType);
        http.Response.StatusCode = status;
        if (status is 204 or 304 || SourceType == typeof(Empty))
        {
            return;
        }

        if (value is IFailure failure)
        {
            await TypedResults.Problem(statusCode: status, type: "about:blank", title: failure.Title, detail: failure.Detail).ExecuteAsync(http);
            return;
        }

        http.Response.ContentType = "application/json";
        if (value is null)
        {
            await http.Response.WriteAsync("null", http.RequestAborted);
        }
        else
        {
            await JsonSerializer.SerializeAsync(http.Response.Body, value, json.GetRequiredTypeInfo(SourceType), http.RequestAborted);
        }
    }

    private static int DefaultStatus(Type type) => type == typeof(Empty) ? 204 : type == typeof(AuthorizationInvocationFilter.AuthenticationRequired) ? 401 : type == typeof(AuthorizationInvocationFilter.PermissionDenied) ? 403 : type == typeof(AuthorizationInvocationFilter.PolicyDenied) ? 400 : typeof(IFailure).IsAssignableFrom(type) ? 422 : 200;
}
