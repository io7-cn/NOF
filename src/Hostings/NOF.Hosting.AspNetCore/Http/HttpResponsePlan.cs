using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Contract;
using NOF.Infrastructure;
using System.Globalization;
using System.Text.Json;

namespace NOF.Hosting.AspNetCore;

/// <summary>One fully merged response. Providers execute only for real requests.</summary>
public sealed class HttpResponsePlan
{
    private readonly HttpValueProvider? _status;
    private readonly HttpValueProvider? _body;
    private readonly IReadOnlyDictionary<string, (HttpValueProvider Value, int Position)> _fields;
    private readonly int _bodyPosition;
    private HttpResponsePlan(Type source, IReadOnlyList<int> statusCodes, HttpValueProvider? status, HttpValueProvider? body, IReadOnlyDictionary<string, HttpValueProvider> headers, IReadOnlyDictionary<string, (HttpValueProvider Value, int Position)> fields, int bodyPosition, HttpBodyDescription? nativeDescription)
    {
        _status = status;
        _body = body;
        Headers = headers;
        _fields = fields;
        _bodyPosition = bodyPosition;
        StatusCodes = statusCodes;
        BodyType = nativeDescription?.BodyType ?? (body is not null ? body.ValueType == typeof(void) ? null : body.ValueType : source == typeof(Empty) ? null : typeof(IFailure).IsAssignableFrom(source) ? typeof(ProblemDetails) : source == typeof(void) ? typeof(object) : source);
        ContentType = BodyType is null ? null : nativeDescription?.ContentType ?? (typeof(ProblemDetails).IsAssignableFrom(BodyType) ? "application/problem+json" : "application/json");
    }

    /// <summary>Gets documented possible status codes.</summary>
    public IReadOnlyList<int> StatusCodes { get; }
    /// <summary>Gets the actual body declaration type, or null for no body.</summary>
    public Type? BodyType { get; }
    /// <summary>Gets the declared media type.</summary>
    public string? ContentType { get; }
    /// <summary>Gets per-name header providers.</summary>
    public IReadOnlyDictionary<string, HttpValueProvider> Headers { get; }
    /// <summary>Gets typed Problem fields for documentation.</summary>
    public IReadOnlyDictionary<string, HttpValueProvider> ProblemFields => _fields.Where(item => BodyType is not null && typeof(ProblemDetails).IsAssignableFrom(BodyType) && (_body is null || item.Value.Position >= _bodyPosition)).ToDictionary(item => item.Key, item => item.Value.Value);

    /// <summary>Merges exact cases and common defaults according to configuration category and native scope order.</summary>
    public static HttpResponsePlan Compile(Type source, string? variant, IEnumerable<HttpResponseRule> rules, InvocationPlan invocation)
    {
        var sorted = rules.Select((rule, index) => (Rule: rule, Index: index)).Where(item => (item.Rule.SourceType == source || item.Rule.SourceType == typeof(object) || item.Rule.SourceType == typeof(IFailure) && typeof(IFailure).IsAssignableFrom(source)) && (item.Rule.Variant is null || item.Rule.Variant == variant) && (item.Rule.FilterType is null || invocation.Filters.Any(filter => filter.FilterType == item.Rule.FilterType && filter.FailureCases.Any(failure => failure.CaseType == source)))).OrderBy(item => item.Rule.Endpoint && item.Rule.FilterType is null ? 4 : item.Rule.FilterType is not null ? 2 : item.Rule.SourceType == typeof(IFailure) || item.Rule.SourceType == typeof(object) ? 1 : 3).ThenBy(item => item.Index).ToArray();
        IReadOnlyList<int> codes = [source == typeof(Empty) ? 204 : source == typeof(AuthorizationInvocationFilter.AuthenticationRequired) ? 401 : source == typeof(AuthorizationInvocationFilter.PermissionDenied) ? 403 : source == typeof(AuthorizationInvocationFilter.PolicyDenied) ? 400 : typeof(IFailure).IsAssignableFrom(source) ? 422 : 200];
        HttpValueProvider? status = null, body = null;
        var bodyPosition = -1;
        var headers = new Dictionary<string, HttpValueProvider>(StringComparer.OrdinalIgnoreCase);
        var fields = new Dictionary<string, (HttpValueProvider Value, int Position)>(StringComparer.Ordinal);
        HttpBodyDescription? native = null;
        for (var position = 0; position < sorted.Length; position++)
        {
            var rule = sorted[position].Rule;
            if (rule.Writer is not null)
            {
                body = new(typeof(IResult), (value, context) => rule.Writer(value, context));
                native = rule.WriterDescription;
                bodyPosition = position;
            }

            foreach (var target in rule.Targets)
            {
                if (target.Key == "status")
                {
                    status = target.Value;
                    codes = rule.StatusCodes!;
                }
                else if (target.Key == "body")
                {
                    body = target.Value;
                    bodyPosition = position;
                    native = null;
                }
                else if (target.Key.StartsWith("header:", StringComparison.OrdinalIgnoreCase))
                {
                    headers[target.Key[7..]] = target.Value;
                }
                else if (target.Key.StartsWith("problem:", StringComparison.Ordinal))
                {
                    fields[target.Key[8..]] = (target.Value, position);
                }
            }
        }

        return new(source, Array.AsReadOnly(codes.ToArray()), status, body, headers, fields, bodyPosition, native);
    }

    /// <summary>Writes the exact selected representation using the host's JSON and Problem services.</summary>
    public async Task WriteAsync(object? source, HttpOutputContext context, JsonSerializerOptions json)
    {
        var http = context.Http;
        var status = _status is null ? StatusCodes[0] : (int)_status.Read(source, context)!;
        if (!StatusCodes.Contains(status))
        {
            throw new InvalidOperationException($"HTTP status {status} was not declared by this mapping.");
        }

        http.Response.StatusCode = status;
        foreach (var header in Headers)
        {
            var value = header.Value.Read(source, context);
            if (header.Value.ValueType == typeof(void) || value is null)
            {
                http.Response.Headers.Remove(header.Key);
            }
            else
            {
                http.Response.Headers[header.Key] = Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }

        if (BodyType is null)
        {
            return;
        }

        var body = _body is not null ? _body.Read(source, context) : source is IFailure failure ? new ProblemDetails
        {
            Title = failure.Title,
            Detail = failure.Detail
        }

        : source;
        if (body is IResult result)
        {
            await result.ExecuteAsync(http);
            return;
        }

        if (body is ProblemDetails problem)
        {
            foreach (var entry in _fields)
            {
                if (_body is not null && entry.Value.Position < _bodyPosition)
                {
                    continue;
                }

                var value = entry.Value.Value.Read(source, context);
                switch (entry.Key)
                {
                    case "type":
                        problem.Type = (string?)value;
                        break;
                    case "title":
                        problem.Title = (string?)value;
                        break;
                    case "detail":
                        problem.Detail = (string?)value;
                        break;
                    case "instance":
                        problem.Instance = (string?)value;
                        break;
                    default:
                        if (entry.Key.StartsWith("extension:", StringComparison.Ordinal))
                        {
                            problem.Extensions[entry.Key[10..]] = value is null ? null : JsonSerializer.SerializeToElement(value, json.GetRequiredTypeInfo(entry.Value.Value.ValueType));
                        }

                        break;
                }
            }

            if (problem.Status is { } declared && declared != status)
            {
                throw new InvalidOperationException("Problem.status differs from the HTTP status.");
            }

            problem.Status = status;
            problem.Type ??= "about:blank";
            var service = http.RequestServices.GetService<IProblemDetailsService>();
            if (service is not null && await service.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = problem }))
            {
                return;
            }

            http.Response.ContentType = "application/problem+json";
            await JsonSerializer.SerializeAsync(http.Response.Body, problem, json.GetRequiredTypeInfo(problem.GetType()), context.CancellationToken);
            return;
        }

        http.Response.ContentType = ContentType;
        if (body is null)
        {
            await http.Response.WriteAsync("null", context.CancellationToken);
        }
        else
        {
            await JsonSerializer.SerializeAsync(http.Response.Body, body, json.GetRequiredTypeInfo(BodyType), context.CancellationToken);
        }
    }
}
