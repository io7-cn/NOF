using Microsoft.Extensions.Options;
using System.Text.Json;

namespace NOF.Infrastructure;

/// <summary>Serializes payloads using an independent snapshot of the host's JSON metadata and policy.</summary>
public class JsonObjectSerializer : IObjectSerializer
{
    /// <summary>Gets the serializer's independent options snapshot.</summary>
    protected JsonSerializerOptions Options { get; }

    /// <summary>Captures the configured host options.</summary>
    [Microsoft.Extensions.DependencyInjection.ActivatorUtilitiesConstructor]
    public JsonObjectSerializer(IOptions<JsonSerializerOptions> options) : this(options.Value)
    {
    }

    /// <summary>Captures explicitly supplied standalone options.</summary>
    public JsonObjectSerializer(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = new JsonSerializerOptions(options);
    }

    /// <inheritdoc />
    public ReadOnlyMemory<byte> Serialize(object? value, Type? runtimeType = null)
    {
        if (value is null)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        runtimeType ??= value.GetType();

        var typeInfo = Options.GetRequiredTypeInfo(runtimeType);
        return JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
    }

    /// <inheritdoc />
    public object? Deserialize(ReadOnlyMemory<byte> data, Type runtimeType)
    {
        ArgumentNullException.ThrowIfNull(runtimeType);

        if (data.IsEmpty)
        {
            return default;
        }

        var typeInfo = Options.GetRequiredTypeInfo(runtimeType);
        return JsonSerializer.Deserialize(data.Span, typeInfo);
    }
}
