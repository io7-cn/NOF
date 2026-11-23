using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using NOF.Abstraction;
using NOF.Contract;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace NOF.Hosting.AspNetCore;

/// <summary>Documents the same immutable binding used to execute each HTTP endpoint.</summary>
public sealed class HttpBindingOpenApiTransformer : IOpenApiOperationTransformer
{
    /// <inheritdoc/>
    public async Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var binding = context.Description.ActionDescriptor.EndpointMetadata.OfType<HttpEndpointBinding>().LastOrDefault();
        if (binding is null)
        {
            return;
        }

        operation.OperationId ??= $"{binding.Operation.Id}:{context.Description.HttpMethod}:{context.Description.RelativePath}";
        operation.Summary ??= binding.Operation.MethodInfo.GetCustomAttribute<SummaryAttribute>()?.Summary;
        operation.Description ??= binding.Operation.MethodInfo.GetCustomAttribute<DescriptionAttribute>()?.Description;
        operation.Parameters = [];
        foreach (var input in binding.Inputs.Where(item => item.Location is HttpInputLocation.Route or HttpInputLocation.Query or HttpInputLocation.Header))
        {
            var schema = await inputSchemaAsync(input);
            if (input.DefaultValue is not null)
            {
                var json = context.ApplicationServices.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
                schema = new OpenApiSchema
                {
                    AllOf = [schema],
                    Default = JsonSerializer.SerializeToNode(input.DefaultValue, json.GetRequiredTypeInfo(input.ValueType))
                };
            }

            operation.Parameters.Add(new OpenApiParameter { Name = input.Name, In = input.Location == HttpInputLocation.Route ? ParameterLocation.Path : input.Location == HttpInputLocation.Header ? ParameterLocation.Header : ParameterLocation.Query, Required = input.Required, Schema = schema });
        }

        var whole = binding.Inputs.FirstOrDefault(item => item.Location == HttpInputLocation.WholeBody);
        var bodyMembers = binding.Inputs.Where(item => item.Location == HttpInputLocation.Body).ToArray();
        operation.RequestBody = null;
        if (whole is not null || bodyMembers.Length > 0)
        {
            IOpenApiSchema bodySchema;
            if (whole is not null)
            {
                bodySchema = await context.GetOrCreateSchemaAsync(whole.ValueType, cancellationToken: cancellationToken);
            }
            else
            {
                var schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.Object,
                    Properties = new Dictionary<string, IOpenApiSchema>(),
                    Required = new HashSet<string>()
                };
                foreach (var input in bodyMembers)
                {
                    schema.Properties[input.Name] = await inputSchemaAsync(input);
                    if (input.Required)
                    {
                        schema.Required.Add(input.Name);
                    }
                }

                bodySchema = schema;
            }

            operation.RequestBody = new OpenApiRequestBody
            {
                Required = whole?.Required ?? bodyMembers.Any(item => item.Required),
                Content = new Dictionary<string, IOpenApiMediaType>
                {
                    [whole?.ContentType ?? "application/json"] = new OpenApiMediaType
                    {
                        Schema = bodySchema
                    }
                }
            };
        }

        var nativeResponses = operation.Responses;
        operation.Responses = new OpenApiResponses();
        foreach (var status in binding.Responses.Select(item => item.StatusCode).Distinct().Order())
        {
            var statusKey = status.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var sources = binding.Responses.Where(item => item.StatusCode == status).ToArray();
            var response = new OpenApiResponse
            {
                Description = string.Join("; ", sources.Select(item => item.Description).Where(item => item is not null).Distinct()) is { Length: > 0 } description ? description : $"HTTP {status}",
                Content = new Dictionary<string, IOpenApiMediaType>(),
                Headers = new Dictionary<string, IOpenApiHeader>()
            };
            foreach (var media in sources.Where(item => item.BodyType is not null).GroupBy(item => item.ContentType!))
            {
                var schemas = new List<IOpenApiSchema>();
                var shapes = media.DistinctBy(item => item.BodyType).ToArray();
                foreach (var shape in shapes)
                {
                    IOpenApiSchema schema;
                    if (shape.SourceType == typeof(void) && shape.BodyType == typeof(object))
                    {
                        schema = new OpenApiSchema { Type = JsonSchemaType.Null };
                    }
                    else if (shapes.Length == 1
                        && context.Description.SupportedResponseTypes.Any(item => item.StatusCode == status && item.Type == shape.BodyType)
                        && nativeResponses?.TryGetValue(statusKey, out var nativeResponse) == true
                        && nativeResponse.Content?.TryGetValue(media.Key, out var nativeMedia) == true
                        && nativeMedia.Schema is { } nativeSchema)
                    {
                        // Native generation has already resolved component references and nullable-property wrappers.
                        schema = nativeSchema;
                    }
                    else
                    {
                        // Additional bodies sharing a status may not have native ApiExplorer schemas.
                        // GetOrCreateSchemaAsync returns an unresolved graph; preserve nullability on this new graph only.
                        var generated = await context.GetOrCreateSchemaAsync(shape.BodyType!, cancellationToken: cancellationToken);
                        var json = context.ApplicationServices.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
                        PreserveResponseNullability(generated, json.GetRequiredTypeInfo(shape.BodyType!));
                        schema = generated;
                    }
                    schemas.Add(schema);
                }

                response.Content[media.Key] = new OpenApiMediaType
                {
                    Schema = schemas.Count == 1 ? schemas[0] : new OpenApiSchema
                    {
                        AnyOf = schemas
                    }
                };
            }

            operation.Responses[statusKey] = response;
        }

        async Task<IOpenApiSchema> inputSchemaAsync(HttpInputDescription input)
        {
            var schema = await context.GetOrCreateSchemaAsync(input.ValueType, cancellationToken: cancellationToken);
            // Wrap references instead of mutating the shared component or conflating presence with nullability.
            return input.AllowsNull ? new OpenApiSchema { AnyOf = [schema, new OpenApiSchema { Type = JsonSchemaType.Null }] } : schema;
        }
    }

    private static void PreserveResponseNullability(IOpenApiSchema schema, JsonTypeInfo typeInfo)
    {
        // Never traverse reference targets: their component definitions can also serve non-nullable responses.
        if (schema is not OpenApiSchema generated)
        {
            return;
        }
        if (typeInfo.ElementType is { } elementType)
        {
            var elementInfo = typeInfo.Options.GetRequiredTypeInfo(elementType);
            if (generated.Items is { } items)
            {
                PreserveResponseNullability(items, elementInfo);
            }
            if (generated.AdditionalProperties is { } additionalProperties)
            {
                PreserveResponseNullability(additionalProperties, elementInfo);
            }
        }
        if (generated.Properties is null)
        {
            return;
        }
        foreach (var property in typeInfo.Properties)
        {
            if (!generated.Properties.TryGetValue(property.Name, out var propertySchema))
            {
                continue;
            }
            PreserveResponseNullability(propertySchema, typeInfo.Options.GetRequiredTypeInfo(property.PropertyType));
            if (property.IsGetNullable && (propertySchema.Type.GetValueOrDefault() & JsonSchemaType.Null) == 0)
            {
                generated.Properties[property.Name] = new OpenApiSchema
                {
                    AnyOf = [propertySchema, new OpenApiSchema { Type = JsonSchemaType.Null }]
                };
            }
        }
    }
}
