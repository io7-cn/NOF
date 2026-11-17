using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NOF.Abstraction;

/// <summary>Writes signed 64-bit integers as invariant decimal strings, preserving JavaScript precision.</summary>
/// <remarks>Reads both decimal strings and integer tokens for compatibility with existing producers.</remarks>
public sealed class JsonStringInt64Converter : JsonConverter<long>
{
    /// <inheritdoc />
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var number))
        {
            return number;
        }
        if (reader.TokenType == JsonTokenType.String
            && long.TryParse(reader.GetString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }
        throw new JsonException("Expected a signed 64-bit integer or decimal integer string.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));

    /// <inheritdoc />
    public override long ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => long.TryParse(reader.GetString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            ? value : throw new JsonException("Expected a signed 64-bit integer property name.");

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
        => writer.WritePropertyName(value.ToString(CultureInfo.InvariantCulture));
}
