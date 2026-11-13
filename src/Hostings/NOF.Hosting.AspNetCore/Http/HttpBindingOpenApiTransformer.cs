using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using NOF.Abstraction;
using System.Text.Json;
using System.Text.Json.Nodes;

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

        operation.Parameters = [];
        foreach (var input in binding.Inputs.Where(item => item.Location is HttpInputLocation.Route or HttpInputLocation.Query or HttpInputLocation.Header))
        {
            var schema = await context.GetOrCreateSchemaAsync(input.ValueType, cancellationToken: cancellationToken);
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
                    schema.Properties[input.Name] = await context.GetOrCreateSchemaAsync(input.ValueType, cancellationToken: cancellationToken);
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

        operation.Responses = new OpenApiResponses();
        foreach (var status in binding.Responses.SelectMany(item => item.StatusCodes).Distinct().Order())
        {
            var sources = binding.Responses.Where(item => item.StatusCodes.Contains(status)).ToArray();
            var response = new OpenApiResponse
            {
                Description = $"HTTP {status}",
                Content = new Dictionary<string, IOpenApiMediaType>(),
                Headers = new Dictionary<string, IOpenApiHeader>()
            };
            foreach (var media in sources.Where(item => item.BodyType is not null).GroupBy(item => item.ContentType!))
            {
                var schemas = new List<IOpenApiSchema>();
                foreach (var shape in media.DistinctBy(item => (item.BodyType, string.Join(',', item.Extensions.Keys))))
                {
                    IOpenApiSchema schema = shape.SourceType == typeof(void) && shape.BodyType == typeof(object) ? new OpenApiSchema
                    {
                        Type = JsonSchemaType.Null
                    }

                    : await context.GetOrCreateSchemaAsync(shape.BodyType!, cancellationToken: cancellationToken);
                    if (shape.Extensions.Count > 0)
                    {
                        var extensions = new OpenApiSchema
                        {
                            Type = JsonSchemaType.Object,
                            Properties = new Dictionary<string, IOpenApiSchema>()
                        };
                        foreach (var extension in shape.Extensions)
                        {
                            extensions.Properties[extension.Key] = await context.GetOrCreateSchemaAsync(extension.Value, cancellationToken: cancellationToken);
                        }

                        schema = new OpenApiSchema
                        {
                            AllOf = [schema, extensions]
                        };
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

            foreach (var header in sources.SelectMany(item => item.Headers).GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                var types = header.Select(item => item.Value).Where(type => type != typeof(void)).Distinct().ToArray();
                if (types.Length == 0)
                {
                    continue;
                }

                var schemas = new List<IOpenApiSchema>();
                foreach (var type in types)
                {
                    schemas.Add(await context.GetOrCreateSchemaAsync(type, cancellationToken: cancellationToken));
                }

                response.Headers[header.Key] = new OpenApiHeader
                {
                    Schema = schemas.Count == 1 ? schemas[0] : new OpenApiSchema
                    {
                        AnyOf = schemas
                    }
                };
            }

            operation.Responses[status.ToString(System.Globalization.CultureInfo.InvariantCulture)] = response;
        }
    }
}
