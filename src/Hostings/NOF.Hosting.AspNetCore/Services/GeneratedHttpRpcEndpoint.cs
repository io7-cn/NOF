using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using NOF.Application;
using NOF.Contract;
using NOF.Infrastructure;
using System.Reflection;
using System.Text.Json;

namespace NOF.Hosting.AspNetCore;

/// <summary>Maps generated Controller RPC routes without building a delegate at runtime.</summary>
public static class GeneratedHttpRpcEndpoint
{
    internal static IReadOnlySet<string> Map(IEndpointRouteBuilder app, RpcServerRegistration registration)
    {
        var mapped = new HashSet<string>(StringComparer.Ordinal);
        if (registration.HandlerMappings is not { } mappings)
        {
            return mapped;
        }

        var routes = registration.ServiceType.Assembly.GetCustomAttributes<GeneratedHttpRpcRouteAttribute>();
        foreach (var attribute in routes)
        {
            var route = attribute.Route;
            if (route.ServiceType != registration.ServiceType
                || !mappings.TryGetValue(route.OperationName, out var mapping))
            {
                continue;
            }

            Map(app, route, mapping);
            mapped.Add(route.OperationName);
        }

        return mapped;
    }

    private static void Map(IEndpointRouteBuilder app, GeneratedHttpRpcRoute route, RpcHandlerMapping mapping)
    {
        var options = app.ServiceProvider.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>()
            .Value.SerializerOptions;
        RequestDelegate handler = context => InvokeAsync(context, route, mapping, options);
        var endpoint = app.MapMethods(route.Template, [route.Verb.ToString().ToUpperInvariant()], handler);
        endpoint.WithMetadata(NofRpcHttpEndpointMetadata.Instance, handler.Method);
        endpoint.AddOpenApiOperationTransformer(async (operation, context, cancellationToken) =>
        {
            operation.Parameters ??= [];
            foreach (var parameter in route.Parameters)
            {
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = parameter.Name,
                    In = parameter.IsPath ? ParameterLocation.Path : ParameterLocation.Query,
                    Required = parameter.IsRequired,
                    Schema = await context.GetOrCreateSchemaAsync(parameter.Type, cancellationToken: cancellationToken).ConfigureAwait(false)
                });
            }

            if (route.GetBodyTypeInfo(options) is { } body)
            {
                var schema = new OpenApiSchema { Type = JsonSchemaType.Object };
                schema.Properties = new Dictionary<string, IOpenApiSchema>();
                schema.Required = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in body.Properties)
                {
                    schema.Properties.Add(property.Name, await context.GetOrCreateSchemaAsync(
                        property.PropertyType, cancellationToken: cancellationToken).ConfigureAwait(false));
                    if (property.IsRequired)
                    {
                        schema.Required.Add(property.Name);
                    }
                }

                operation.RequestBody = new OpenApiRequestBody
                {
                    Required = true,
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["application/json"] = new() { Schema = schema }
                    }
                };
            }

            var responseSchema = await context.GetOrCreateSchemaAsync(
                route.ResponseType, cancellationToken: cancellationToken).ConfigureAwait(false);
            operation.Responses ??= new OpenApiResponses();
            operation.Responses["200"] = CreateResponse("OK", responseSchema);
            operation.Responses["400"] = new OpenApiResponse { Description = "Bad Request" };
            operation.Responses["401"] = CreateResponse("Unauthorized", responseSchema);
            operation.Responses["403"] = CreateResponse("Forbidden", responseSchema);
            if (route.HasBody)
            {
                operation.Responses["415"] = new OpenApiResponse { Description = "Unsupported Media Type" };
            }
        });
    }

    private static OpenApiResponse CreateResponse(string description, IOpenApiSchema schema)
        => new()
        {
            Description = description,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new() { Schema = schema }
            }
        };

    private static async Task InvokeAsync(
        HttpContext context, GeneratedHttpRpcRoute route, RpcHandlerMapping mapping, JsonSerializerOptions options)
    {
        if (route.HasBody && !context.Request.HasJsonContentType())
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return;
        }

        object? request;
        try
        {
            request = await route.BindAsync(
                name => context.Request.RouteValues[name]?.ToString(),
                name => context.Request.Query[name].FirstOrDefault(),
                context.Request.ReadFromJsonAsync,
                options,
                context.RequestAborted).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        if (request is null)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var adapter = context.RequestServices.GetRequiredService<HttpRequestInboundAdapter>();
        var execution = await adapter.InvokeAsync(
            context, route.ServiceType, route.OperationName, request, mapping, context.RequestAborted).ConfigureAwait(false);
        var response = execution.Response ?? throw new InvalidOperationException("The RPC endpoint did not return a result.");
        context.Response.StatusCode = context.Items.TryGetValue(NOFInfrastructureConstants.Transport.Headers.HttpStatusCode, out var status)
            && status is int code
            ? code
            : StatusCodes.Status200OK;
        context.Response.ContentType = "application/json; charset=utf-8";
        await route.WriteResponseAsync(context.Response.Body, response, options, context.RequestAborted).ConfigureAwait(false);
    }
}
