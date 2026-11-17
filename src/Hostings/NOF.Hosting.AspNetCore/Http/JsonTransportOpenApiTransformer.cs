using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using NOF.Abstraction;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace NOF.Hosting.AspNetCore;

/// <summary>Documents NOF primitive converters using the same JSON metadata as HTTP execution.</summary>
public sealed class JsonTransportOpenApiTransformer : IOpenApiSchemaTransformer
{
    /// <inheritdoc />
    public async Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;
        var converter = context.JsonPropertyInfo?.CustomConverter ?? context.JsonTypeInfo.Options.GetRequiredTypeInfo(type).Converter;
        // Native schema export omits unconstrained item schemas for custom converters.
        // Recover those edges from JSON metadata so nested NOF primitives are visited.
        if (context.JsonTypeInfo.ElementType is { } elementType)
        {
            var element = Nullable.GetUnderlyingType(elementType) ?? elementType;
            var elementConverter = context.JsonTypeInfo.Options.GetRequiredTypeInfo(element).Converter;
            if (elementConverter is JsonStringInt64Converter or IJsonPrimitiveConverter)
            {
                if (context.JsonTypeInfo.Kind == JsonTypeInfoKind.Enumerable && schema.Items is null)
                {
                    schema.Items = await context.GetOrCreateSchemaAsync(elementType, cancellationToken: cancellationToken);
                }
                else if (context.JsonTypeInfo.Kind == JsonTypeInfoKind.Dictionary && schema.AdditionalProperties is null)
                {
                    schema.AdditionalProperties = await context.GetOrCreateSchemaAsync(elementType, cancellationToken: cancellationToken);
                }
            }
        }
        if (type.IsEnum && schema.Enum is { Count: > 0 }
            && schema.Enum.All(value => value?.GetValueKind() is JsonValueKind.String or JsonValueKind.Null))
        {
            schema.Type = JsonSchemaType.String | (schema.Type.GetValueOrDefault() & JsonSchemaType.Null);
        }
        if (converter is JsonStringInt64Converter)
        {
            schema.Type = JsonSchemaType.String | (schema.Type.GetValueOrDefault() & JsonSchemaType.Null)
                | (Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) is not null ? JsonSchemaType.Null : 0);
            schema.Format = null;
            schema.Pattern = "^[+-]?[0-9]+$";
            schema.Minimum = null;
            schema.Maximum = null;
        }
        else if (converter is IJsonPrimitiveConverter primitive)
        {
            var primitiveSchema = await context.GetOrCreateSchemaAsync(primitive.PrimitiveType, cancellationToken: cancellationToken);
            schema.Type = null;
            schema.Properties = null;
            schema.AnyOf = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) is not null
                ? [primitiveSchema, new OpenApiSchema { Type = JsonSchemaType.Null }]
                : [primitiveSchema];
        }
    }
}
