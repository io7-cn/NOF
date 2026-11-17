using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing.Patterns;
using NOF.Abstraction;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace NOF.Hosting.AspNetCore;

/// <summary>Reads each request body at most once with actual host JSON metadata.</summary>
public sealed class HttpRequestReadContext(HttpContext http, JsonSerializerOptions json)
{
    private JsonNode? _body;
    private bool _bodyRead;
    /// <summary>Gets the native HTTP request.</summary>
    public HttpContext Http => http;

    private async ValueTask<JsonNode?> ReadBody()
    {
        if (!_bodyRead)
        {
            if (!http.Request.HasJsonContentType())
            {
                throw new BadHttpRequestException("Expected application/json.", 415);
            }

            try
            {
                _body = await JsonNode.ParseAsync(http.Request.Body, cancellationToken: http.RequestAborted);
            }
            catch (JsonException exception)
            {
                throw new BadHttpRequestException(exception.Message, exception);
            }
            _bodyRead = true;
        }

        return _body;
    }

    /// <summary>Reads a JSON object for member-based construction.</summary>
    public async ValueTask<JsonObject> ObjectBody() => await ReadBody() as JsonObject ?? throw new BadHttpRequestException("Expected a JSON object.");
    /// <summary>Reads a statically described JSON body, including scalar and collection shapes.</summary>
    public async ValueTask<T> Body<T>() => Deserialize(await ReadBody(), json.GetRequiredTypeInfo<T>());

    /// <summary>Reports malformed JSON as input failure while allowing user-hook exceptions to propagate.</summary>
    public static T Deserialize<T>(JsonNode? node, JsonTypeInfo<T> metadata)
    {
        try
        {
            return node.Deserialize(metadata) ?? throw new BadHttpRequestException("The JSON body cannot be null.");
        }
        catch (JsonException exception)
        {
            throw new BadHttpRequestException(exception.Message, exception);
        }
    }
    /// <summary>Reads a scalar or repeated source using the host's type metadata.</summary>
    public ValueTask<T> Value<T>(HttpInputLocation source, string name)
    {
        var strings = source switch
        {
            HttpInputLocation.Route => new Microsoft.Extensions.Primitives.StringValues(http.Request.RouteValues[name]?.ToString()),
            HttpInputLocation.Header => http.Request.Headers[name],
            HttpInputLocation.Query => http.Request.Query[name],
            _ => throw new ArgumentOutOfRangeException(nameof(source), "Only route, query and header inputs are scalar sources.")
        };
        if (strings.Count == 0)
        {
            throw new BadHttpRequestException($"Missing {source} value '{name}'.");
        }

        return ValueTask.FromResult((T)Parse(strings, typeof(T), json)!);
    }

    /// <summary>Converts an external scalar without culture-sensitive inference or reflection setters.</summary>
    public static object? Parse(Microsoft.Extensions.Primitives.StringValues strings, Type type, JsonSerializerOptions json)
    {
        try
        {
            return ParseCore(strings, type, json);
        }
        catch (Exception exception) when (exception is JsonException or FormatException or OverflowException or ArgumentException)
        {
            throw new BadHttpRequestException(exception.Message, exception);
        }
    }

    private static object? ParseCore(Microsoft.Extensions.Primitives.StringValues strings, Type type, JsonSerializerOptions json)
    {
        var info = json.GetRequiredTypeInfo(type);
        var element = type.IsArray ? type.GetElementType() : type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) ? type.GenericTypeArguments[0] : null;
        if (element is not null)
        {
            var array = new JsonArray(strings.Select(value => Scalar(value, element, json)).ToArray());
            return array.Deserialize(info);
        }

        if (strings.Count != 1)
        {
            throw new BadHttpRequestException("A scalar input cannot contain multiple values.");
        }

        return Scalar(strings[0], type, json)?.Deserialize(info);
    }

    private static JsonNode? Scalar(string? value, Type type, JsonSerializerOptions json)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (value is null)
        {
            return null;
        }

        if (json.GetRequiredTypeInfo(type).Converter is IJsonPrimitiveConverter primitive)
        {
            return Scalar(value, primitive.PrimitiveType, json);
        }

        if (type.IsEnum)
        {
            var metadata = json.GetRequiredTypeInfo(type);
            var node = JsonValue.Create(value);
            try
            {
                // Honor wire names and integer rejection from the host's enum converter.
                node.Deserialize(metadata);
                return node;
            }
            catch (JsonException)
            {
                var parsed = Enum.Parse(type, value, true);
                var serialized = JsonSerializer.SerializeToNode(parsed, metadata);
                // Numeric default enums still accept CLR names in textual HTTP sources.
                if (serialized?.GetValueKind() == JsonValueKind.Number)
                {
                    return serialized;
                }
                throw;
            }
        }

        if (type == typeof(bool))
        {
            return JsonValue.Create(bool.Parse(value));
        }

        if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) || type == typeof(float) || type == typeof(double) || type == typeof(decimal))
        {
            return JsonNode.Parse(value);
        }

        return JsonValue.Create(value);
    }
}
