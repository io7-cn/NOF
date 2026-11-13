using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using NOF.Infrastructure;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace NOF.Hosting.AspNetCore;

/// <summary>One resolved CLR/JSON member and external source.</summary>
public sealed record HttpBoundMember(string ClrName, string JsonName, Type ValueType, HttpInputLocation Source, string SourceName, bool Required, bool AllowsNull, bool HasDefault, object? DefaultValue);

/// <summary>A frozen typed operation binding, built only after native endpoint conventions finish.</summary>
public sealed class HttpEndpointExecutor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest>
{
    private readonly JsonSerializerOptions _json;
    private readonly Func<HttpRequestReadContext, ValueTask<TRequest>>? _constructor;
    private readonly IReadOnlyList<HttpBoundMember> _members;
    private readonly IReadOnlyDictionary<object, Func<HttpContext, object?>> _items;
    private readonly Func<HttpContext, string?>? _tenant;
    private readonly IReadOnlyList<HttpResponseRule> _rules;
    private readonly InvocationPlan _plan;
    private readonly HttpResponseSelector? _selector;
    private readonly StringComparison _queryComparison;
    private readonly StringComparison _routeComparison;
    private HttpEndpointExecutor(JsonSerializerOptions json, Func<HttpRequestReadContext, ValueTask<TRequest>>? constructor, IReadOnlyList<HttpBoundMember> members, IReadOnlyDictionary<object, Func<HttpContext, object?>> items, Func<HttpContext, string?>? tenant, IReadOnlyList<HttpResponseRule> rules, InvocationPlan plan, HttpResponseSelector? selector, HttpEndpointBinding binding, StringComparison queryComparison, StringComparison routeComparison)
    {
        _json = json;
        _constructor = constructor;
        _members = members;
        _items = items;
        _tenant = tenant;
        _rules = rules;
        _plan = plan;
        _selector = selector;
        Binding = binding;
        _queryComparison = queryComparison;
        _routeComparison = routeComparison;
    }

    /// <summary>Gets the common execution/documentation contract.</summary>
    public HttpEndpointBinding Binding { get; }

    /// <summary>Compiles metadata without invoking constructors, providers, handlers or selectors.</summary>
    public static HttpEndpointExecutor<TRequest> Compile(EndpointBuilder endpoint, ServiceOperationDescriptor operation, IReadOnlyList<string> methods)
    {
        var services = endpoint.ApplicationServices;
        var json = services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        var typeInfo = json.GetRequiredTypeInfo<TRequest>();
        using var scope = services.CreateScope();
        var plan = scope.ServiceProvider.GetRequiredService<IInvocationPlanProvider>().Describe(operation, InvocationOrigin.Http);
        var options = endpoint.Metadata.OfType<HttpRequestOptions>().ToArray();
        var routeComparison = options.LastOrDefault(item => item.RouteNameComparison.HasValue)?.RouteNameComparison ?? StringComparison.OrdinalIgnoreCase;
        var queryComparison = options.LastOrDefault(item => item.QueryNameComparison.HasValue)?.QueryNameComparison ?? StringComparison.OrdinalIgnoreCase;
        var constructors = endpoint.Metadata.OfType<HttpRequestBuilder<TRequest>>().ToArray();
        Func<HttpRequestReadContext, ValueTask<TRequest>>? constructor = null;
        HttpBodyDescription? bodyDescription = null;
        var overrides = new Dictionary<string, HttpRequestMember>();
        var inputs = new List<HttpInputDescription>();
        foreach (var configuration in constructors)
        {
            if (configuration.ResetsConstruction)
            {
                constructor = configuration.Constructor;
                bodyDescription = configuration.BodyDescription;
                inputs.Clear();
                inputs.AddRange(configuration.Inputs);
                if (constructor is not null)
                {
                    overrides.Clear();
                }
            }

            if (constructor is not null && configuration.Members.Count > 0)
            {
                throw new InvalidOperationException("Member cannot override an inherited complete Map. Use ByConvention.");
            }

            foreach (var member in configuration.Members)
            {
                overrides[member.Key] = member.Value;
            }
        }

        var route = ((RouteEndpointBuilder)endpoint).RoutePattern;
        var members = new List<HttpBoundMember>();
        if (constructor is null)
        {
            var properties = typeof(TRequest).GetProperties(BindingFlags.Instance | BindingFlags.Public);
            var parameters = typeof(TRequest).GetConstructors().OrderByDescending(item => item.GetParameters().Length).FirstOrDefault()?.GetParameters() ?? [];
            foreach (var property in properties)
            {
                if (property.GetIndexParameters().Length > 0 || property.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition == JsonIgnoreCondition.Always)
                {
                    continue;
                }

                var jsonName = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? json.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name;
                var metadata = typeInfo.Properties.FirstOrDefault(item => item.Name == jsonName);
                if (metadata is null)
                {
                    continue;
                }

                var parameter = parameters.FirstOrDefault(item => string.Equals(item.Name, property.Name, StringComparison.OrdinalIgnoreCase));
                var required = metadata.IsRequired || parameter is { IsOptional: false };
                overrides.TryGetValue(property.Name, out var declared);
                var routeName = route.Parameters.FirstOrDefault(item => string.Equals(item.Name, property.Name, routeComparison))?.Name;
                var source = declared?.Source ?? (routeName is not null ? HttpInputLocation.Route : methods.All(item => item is "GET" or "HEAD" or "DELETE") ? HttpInputLocation.Query : HttpInputLocation.Body);
                var sourceName = declared?.Name ?? (source == HttpInputLocation.Route ? routeName! : source == HttpInputLocation.Body ? jsonName : property.Name);
                if (source == HttpInputLocation.Route && !route.Parameters.Any(item => string.Equals(item.Name, sourceName, routeComparison)))
                {
                    throw new InvalidOperationException($"Route has no parameter '{sourceName}'.");
                }

                var hasDefault = declared?.HasDefault == true || parameter?.HasDefaultValue == true;
                var defaultValue = declared?.HasDefault == true ? declared.DefaultValue : parameter?.HasDefaultValue == true ? parameter.DefaultValue : null;
                members.Add(new(property.Name, jsonName, property.PropertyType, source, sourceName, required, metadata.IsSetNullable, hasDefault, defaultValue));
                if (source != HttpInputLocation.Body)
                {
                    inputs.Add(new(sourceName, source, property.PropertyType, source == HttpInputLocation.Route || required && !hasDefault, defaultValue));
                }
            }

            foreach (var name in overrides.Keys)
            {
                if (!members.Any(item => item.ClrName == name))
                {
                    throw new InvalidOperationException($"Unknown serialized request member '{name}'.");
                }
            }
        }
        else if (inputs.Any(item => item.Location == HttpInputLocation.Route && !route.Parameters.Any(parameter => string.Equals(parameter.Name, item.Name, routeComparison))))
        {
            throw new InvalidOperationException("A complete request constructor names a missing route parameter.");
        }

        var items = new Dictionary<object, Func<HttpContext, object?>>();
        Func<HttpContext, string?>? tenant = null;
        HttpInputDescription? tenantInput = null;
        var contextInputs = new Dictionary<object, IReadOnlyList<HttpInputDescription>>();
        foreach (var context in endpoint.Metadata.OfType<HttpContextBuilder>())
        {
            foreach (var item in context.Items)
            {
                items[item.Key] = item.Value;
                contextInputs[item.Key] = context.Inputs.GetValueOrDefault(item.Key) ?? [];
            }

            if (context.Tenant is not null)
            {
                tenant = context.Tenant;
                tenantInput = context.TenantInput;
            }
        }

        inputs.AddRange(contextInputs.Values.SelectMany(value => value));
        if (tenantInput is not null)
        {
            inputs.Add(tenantInput);
        }

        var cases = operation.ResultCases.Select(item => item.CaseType).Concat(plan.FailureCases.Select(item => item.CaseType)).Distinct().ToList();
        if (operation.AllowsNullResponse)
        {
            cases.Add(typeof(void));
        }

        var rules = endpoint.Metadata.OfType<HttpResponseRule>().Where(rule => rule.FilterType is null || plan.Filters.Any(filter => filter.FilterType == rule.FilterType)).Select(rule =>
        {
            var copy = new HttpResponseRule
            {
                SourceType = rule.SourceType,
                Variant = rule.Variant,
                FilterType = rule.FilterType,
                Endpoint = rule.Endpoint,
                StatusCodes = rule.StatusCodes,
                Writer = rule.Writer,
                WriterDescription = rule.WriterDescription
            };
            foreach (var target in rule.Targets)
            {
                copy.Targets[target.Key] = target.Value;
            }

            return copy;
        }).ToArray();
        foreach (var rule in rules.Where(rule => rule.Endpoint && rule.FilterType is null))
        {
            if (rule.SourceType != typeof(IFailure) && rule.SourceType != typeof(object) && !cases.Contains(rule.SourceType))
            {
                throw new InvalidOperationException($"Endpoint response '{rule.SourceType}' cannot occur in '{operation.Id}'.");
            }
        }

        var responses = new List<HttpResponseDescription>();
        foreach (var source in cases)
        {
            var variants = rules.Where(rule => rule.SourceType == source && rule.Variant is not null).Select(rule => rule.Variant).Distinct().Prepend(null);
            foreach (var variant in variants)
            {
                var merged = HttpResponsePlan.Compile(source, variant, rules, plan);
                if (merged.BodyType is not null && merged.BodyType != typeof(void) && merged.BodyType != typeof(object))
                {
                    _ = json.GetRequiredTypeInfo(merged.BodyType);
                }

                if (merged.StatusCodes.Any(code => code is 204 or 304) && merged.BodyType is not null)
                {
                    throw new InvalidOperationException("204/304 responses must explicitly have no body.");
                }

                responses.Add(new(source, variant, merged.StatusCodes, merged.BodyType, merged.ContentType, merged.Headers.ToFrozenDictionary(item => item.Key, item => item.Value.ValueType, StringComparer.OrdinalIgnoreCase), merged.ProblemFields.Where(item => item.Key.StartsWith("extension:", StringComparison.Ordinal)).ToFrozenDictionary(item => item.Key[10..], item => item.Value.ValueType)));
            }
        }

        if (constructor is not null && bodyDescription is not null)
        {
            inputs.Add(new("", HttpInputLocation.WholeBody, bodyDescription.BodyType, bodyDescription.Required, ContentType: bodyDescription.ContentType));
        }

        foreach (var member in members.Where(item => item.Source == HttpInputLocation.Body))
        {
            inputs.Add(new(member.SourceName, HttpInputLocation.Body, member.ValueType, member.Required && !member.HasDefault, member.DefaultValue));
        }

        var binding = new HttpEndpointBinding(operation, Array.AsReadOnly(inputs.DistinctBy(item => (item.Name, item.Location)).ToArray()), Array.AsReadOnly(responses.ToArray()), Array.AsReadOnly(plan.FailureCases.Select(item => item.CaseType).ToArray()));
        return new(json, constructor, members.AsReadOnly(), items, tenant, Array.AsReadOnly(rules), plan, endpoint.Metadata.OfType<HttpResponseSelector>().LastOrDefault(), binding, queryComparison, routeComparison);
    }

    /// <summary>Binds input, invokes the common executor, and writes the selected actual result.</summary>
    public async Task ExecuteAsync(HttpContext http)
    {
        TRequest input;
        try
        {
            input = await ReadAsync(new(http, _json));
        }
        catch (Exception exception) when (exception is BadHttpRequestException)
        {
            var status = exception is BadHttpRequestException bad ? bad.StatusCode : 400;
            await TypedResults.Problem(statusCode: status, type: "about:blank", title: "Invalid request", detail: exception.Message).ExecuteAsync(http);
            return;
        }

        var adapter = http.RequestServices.GetRequiredService<HttpRequestInboundAdapter>();
        await adapter.AuthenticateAsync(http, _plan, http.RequestAborted);
        var context = Context.Empty;
        foreach (var item in _items)
        {
            context = context.WithItem(item.Key, item.Value(http));
        }

        if (_tenant is not null)
        {
            context = context.WithTenantId(_tenant(http));
        }

        var output = new HttpOutputContext(http, context, Binding.Operation, http.RequestAborted);
        var result = InvocationResult.Value(await adapter.ExecuteAsync(Binding.Operation, input!, http, context, http.RequestAborted));
        var reference = _selector?.Select(result, new HttpResponseMappings());
        var source = result is null ? typeof(void) : Binding.Responses.Select(item => item.SourceType).Distinct().OrderBy(type => type == result.GetType() ? 0 : 1).FirstOrDefault(type => type.IsInstanceOfType(result)) ?? result.GetType();
        if (reference is not null && reference.SourceType != source && !(reference.Variant is null && reference.SourceType == result?.GetType()))
        {
            throw new InvalidOperationException("The selected response reference does not match the actual source.");
        }

        if (reference?.Variant is { } name && !Binding.Responses.Any(item => item.SourceType == source && item.Variant == name))
        {
            throw new InvalidOperationException($"Unknown response variant '{name}'.");
        }

        var response = HttpResponsePlan.Compile(source, reference?.Variant, _rules, _plan);
        await response.WriteAsync(result, output, _json);
    }

    private async ValueTask<TRequest> ReadAsync(HttpRequestReadContext read)
    {
        if (_constructor is not null)
        {
            return await _constructor(read);
        }

        var objectValue = new JsonObject();
        JsonObject? body = null;
        if (_members.Any(item => item.Source == HttpInputLocation.Body))
        {
            body = read.Http.Request.ContentLength == 0 && !_members.Any(item => item.Source == HttpInputLocation.Body && item.Required && !item.HasDefault) ? new JsonObject() : await read.ObjectBody();
            var unknown = body.Where(item => !_members.Any(member => member.Source == HttpInputLocation.Body && member.SourceName.Equals(item.Key, _json.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))).ToArray();
            var handling = _json.GetRequiredTypeInfo<TRequest>().UnmappedMemberHandling ?? _json.UnmappedMemberHandling;
            if (handling == JsonUnmappedMemberHandling.Disallow && unknown.Length > 0)
            {
                throw new BadHttpRequestException($"Unknown JSON member '{unknown[0].Key}'.");
            }
        }

        foreach (var member in _members)
        {
            JsonNode? value = null;
            bool found;
            if (member.Source == HttpInputLocation.Body)
            {
                var pair = body!.FirstOrDefault(item => string.Equals(item.Key, member.SourceName, _json.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
                found = pair.Key is not null;
                value = pair.Value?.DeepClone();
            }
            else
            {
                var values = member.Source switch
                {
                    HttpInputLocation.Route => new StringValues(read.Http.Request.RouteValues.FirstOrDefault(item => string.Equals(item.Key, member.SourceName, _routeComparison)).Value?.ToString()),
                    HttpInputLocation.Header => read.Http.Request.Headers[member.SourceName],
                    _ => read.Http.Request.Query.FirstOrDefault(item => string.Equals(item.Key, member.SourceName, _queryComparison)).Value
                };
                found = values.Count > 0;
                if (found)
                {
                    value = JsonSerializer.SerializeToNode(HttpRequestReadContext.Parse(values, member.ValueType, _json), _json.GetRequiredTypeInfo(member.ValueType));
                }
            }

            if (!found && member.HasDefault)
            {
                value = JsonSerializer.SerializeToNode(member.DefaultValue, _json.GetRequiredTypeInfo(member.ValueType));
                found = true;
            }

            if (!found && member.Required)
            {
                throw new BadHttpRequestException($"Missing {member.Source} member '{member.SourceName}'.");
            }

            if (found && value is null && !member.AllowsNull)
            {
                throw new BadHttpRequestException($"Member '{member.SourceName}' cannot be null.");
            }

            if (found)
            {
                objectValue[member.JsonName] = value;
            }
        }

        return HttpRequestReadContext.Deserialize(objectValue, _json.GetRequiredTypeInfo<TRequest>());
    }
}
