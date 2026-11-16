using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using NOF.Sample.Endpoints.Models;
using System.Reflection;
using System.Text.Json.Nodes;

namespace NOF.Sample.OpenApi;

/// <summary>Documents the Sample's transport-specific enum and header shapes.</summary>
public sealed class SampleOpenApiSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        if (context.JsonTypeInfo.Type == typeof(MessageReceiptKind))
        {
            schema.Enum = Enum.GetValues<MessageReceiptKind>().Select(kind => (JsonNode)JsonValue.Create((int)kind)!).ToList();
            schema.Description = "Known business projections: " + string.Join(", ",
                Enum.GetValues<MessageReceiptKind>().Select(kind => $"{(int)kind} = {kind}"))
                + ". Consumers should display an unknown future numeric kind without reinterpreting it.";
        }
        // Generic nullability is erased by the native dictionary schema; keep this HTTP field explicit.
        if (context.JsonPropertyInfo?.AttributeProvider is PropertyInfo property
            && property.DeclaringType == typeof(DemoOutboxEntry) && property.Name == nameof(DemoOutboxEntry.Headers))
        {
            schema.AdditionalProperties = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null };
        }
        return Task.CompletedTask;
    }
}
