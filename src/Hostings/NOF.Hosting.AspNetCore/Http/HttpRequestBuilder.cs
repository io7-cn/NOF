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

/// <summary>Options merged independently through native route groups.</summary>
public sealed class HttpRequestOptions
{
    /// <summary>Gets or sets explicitly overridden route name comparison.</summary>
    public StringComparison? RouteNameComparison { get; set; }
    /// <summary>Gets or sets explicitly overridden query name comparison.</summary>
    public StringComparison? QueryNameComparison { get; set; }
}

/// <summary>A declared request member source.</summary>
public sealed record HttpRequestMember(string Member, HttpInputLocation Source, string Name, Type ValueType, bool HasDefault = false, object? DefaultValue = null);

/// <summary>Typed request construction; configuration never executes the map delegate.</summary>
public sealed class HttpRequestBuilder<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] T>
{
    /// <summary>Gets member source overrides.</summary>
    public Dictionary<string, HttpRequestMember> Members { get; } = new(StringComparer.Ordinal);
    /// <summary>Gets a full constructor, if explicitly configured.</summary>
    public Func<HttpRequestReadContext, ValueTask<T>>? Constructor { get; set; }
    /// <summary>Gets complete constructor input metadata.</summary>
    public List<HttpInputDescription> Inputs { get; } = [];
    /// <summary>Gets an optional whole-body input contract.</summary>
    public HttpBodyDescription? BodyDescription { get; set; }
    /// <summary>Gets whether this declaration explicitly switches construction modes.</summary>
    public bool ResetsConstruction { get; private set; }

    /// <summary>Selects a request member without compiling an expression.</summary>
    public HttpRequestMemberBuilder<T, TValue> Member<TValue>(Expression<Func<T, TValue>> member)
    {
        if (Constructor is not null)
        {
            throw new InvalidOperationException("Member cannot modify a complete Map. Select ByConvention first.");
        }

        var expression = member.Body is UnaryExpression unary ? unary.Operand : member.Body;
        if (expression is not MemberExpression { Member: PropertyInfo property, Expression: ParameterExpression })
        {
            throw new ArgumentException("Select a direct request property.", nameof(member));
        }

        return new(this, property.Name);
    }

    /// <summary>Restores the default construction plan.</summary>
    public HttpRequestBuilder<T> ByConvention()
    {
        Constructor = null;
        Members.Clear();
        Inputs.Clear();
        BodyDescription = null;
        ResetsConstruction = true;
        return this;
    }

    /// <summary>Uses the entire JSON body as the request.</summary>
    public HttpRequestBuilder<T> Body()
    {
        ResetsConstruction = true;
        Members.Clear();
        Inputs.Clear();
        SetBody(typeof(T));
        Constructor = async read => await read.Body<T>();
        return this;
    }

    /// <summary>Starts a complete typed constructor from a route value.</summary>
    public HttpRequestSourceBuilder<T, TValue> Route<TValue>(string name) => Source<TValue>(HttpInputLocation.Route, name);
    /// <summary>Starts a complete typed constructor from a query value.</summary>
    public HttpRequestSourceBuilder<T, TValue> Query<TValue>(string name) => Source<TValue>(HttpInputLocation.Query, name);
    /// <summary>Starts a complete typed constructor from a header.</summary>
    public HttpRequestSourceBuilder<T, TValue> Header<TValue>(string name) => Source<TValue>(HttpInputLocation.Header, name);
    /// <summary>Starts a complete constructor from a whole JSON body.</summary>
    public HttpRequestSourceBuilder<T, TValue> Body<TValue>()
    {
        SetBody(typeof(TValue));
        return new(this, read => read.Body<TValue>());
    }

    /// <summary>Uses an opaque constructor and its explicit input contract.</summary>
    public HttpRequestBuilder<T> Custom(Func<HttpContext, CancellationToken, ValueTask<T>> constructor, HttpBodyDescription? body, params HttpInputDescription[] inputs)
    {
        SetConstructor(read => constructor(read.Http, read.Http.RequestAborted));
        BodyDescription = body;
        Inputs.AddRange(inputs);
        return this;
    }

    /// <summary>Installs the complete construction slot.</summary>
    public void SetConstructor(Func<HttpRequestReadContext, ValueTask<T>> constructor)
    {
        if (Members.Count > 0)
        {
            throw new InvalidOperationException("A declaration cannot combine Member and a complete Map.");
        }

        Constructor = constructor;
        ResetsConstruction = true;
    }

    /// <summary>Declares the single whole-body input for a complete constructor.</summary>
    public void SetBody(Type type)
    {
        if (BodyDescription is not null)
        {
            throw new InvalidOperationException("A complete request constructor can read only one whole body.");
        }

        BodyDescription = new(type);
    }

    private HttpRequestSourceBuilder<T, TValue> Source<TValue>(HttpInputLocation source, string name)
    {
        Inputs.Add(new(name, source, typeof(TValue), true));
        return new(this, read => read.Value<TValue>(source, name));
    }
}

/// <summary>Assigns a declared source to a single request member.</summary>
public sealed class HttpRequestMemberBuilder<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] T, TValue>(HttpRequestBuilder<T> parent, string name)
{
    /// <summary>Binds a native route parameter.</summary>
    public HttpRequestBuilder<T> FromRoute(string source) => Set(HttpInputLocation.Route, source);
    /// <summary>Binds a query parameter.</summary>
    public HttpRequestBuilder<T> FromQuery(string source) => Set(HttpInputLocation.Query, source);
    /// <summary>Binds a query parameter with a documented default.</summary>
    public HttpRequestBuilder<T> FromQuery(string source, TValue defaultValue) => Set(HttpInputLocation.Query, source, true, defaultValue);
    /// <summary>Binds one header.</summary>
    public HttpRequestBuilder<T> FromHeader(string source) => Set(HttpInputLocation.Header, source);
    /// <summary>Binds one top-level JSON property.</summary>
    public HttpRequestBuilder<T> FromBody(string source) => Set(HttpInputLocation.Body, source);
    private HttpRequestBuilder<T> Set(HttpInputLocation source, string sourceName, bool hasDefault = false, object? defaultValue = null)
    {
        parent.Members[name] = new(name, source, sourceName, typeof(TValue), hasDefault, defaultValue);
        return parent;
    }
}

/// <summary>One explicitly typed input in a complete Map constructor.</summary>
public sealed class HttpRequestSourceBuilder<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] T, T1>(HttpRequestBuilder<T> parent, Func<HttpRequestReadContext, ValueTask<T1>> first)
{
    /// <summary>Adds a whole JSON body.</summary>
    public HttpRequestSourceBuilder<T, T1, T2> Body<T2>()
    {
        parent.SetBody(typeof(T2));
        return new(parent, first, read => read.Body<T2>());
    }

    /// <summary>Adds a route input.</summary>
    public HttpRequestSourceBuilder<T, T1, T2> Route<T2>(string name)
    {
        parent.Inputs.Add(new(name, HttpInputLocation.Route, typeof(T2), true));
        return new(parent, first, read => read.Value<T2>(HttpInputLocation.Route, name));
    }

    /// <summary>Adds a query input.</summary>
    public HttpRequestSourceBuilder<T, T1, T2> Query<T2>(string name)
    {
        parent.Inputs.Add(new(name, HttpInputLocation.Query, typeof(T2), true));
        return new(parent, first, read => read.Value<T2>(HttpInputLocation.Query, name));
    }

    /// <summary>Adds a header input.</summary>
    public HttpRequestSourceBuilder<T, T1, T2> Header<T2>(string name)
    {
        parent.Inputs.Add(new(name, HttpInputLocation.Header, typeof(T2), true));
        return new(parent, first, read => read.Value<T2>(HttpInputLocation.Header, name));
    }

    /// <summary>Completes a one-input constructor.</summary>
    public HttpRequestBuilder<T> Map(Func<T1, T> map)
    {
        parent.SetConstructor(async read => map(await first(read)));
        return parent;
    }
}

/// <summary>Two explicitly typed inputs in a complete Map constructor.</summary>
public sealed class HttpRequestSourceBuilder<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] T, T1, T2>(HttpRequestBuilder<T> parent, Func<HttpRequestReadContext, ValueTask<T1>> first, Func<HttpRequestReadContext, ValueTask<T2>> second)
{
    /// <summary>Adds a whole JSON body.</summary>
    public HttpRequestSourceBuilder<T, T1, T2, T3> Body<T3>()
    {
        parent.SetBody(typeof(T3));
        return new(parent, first, second, read => read.Body<T3>());
    }

    /// <summary>Adds a route input.</summary>
    public HttpRequestSourceBuilder<T, T1, T2, T3> Route<T3>(string name)
    {
        parent.Inputs.Add(new(name, HttpInputLocation.Route, typeof(T3), true));
        return new(parent, first, second, read => read.Value<T3>(HttpInputLocation.Route, name));
    }

    /// <summary>Adds a query input.</summary>
    public HttpRequestSourceBuilder<T, T1, T2, T3> Query<T3>(string name)
    {
        parent.Inputs.Add(new(name, HttpInputLocation.Query, typeof(T3), true));
        return new(parent, first, second, read => read.Value<T3>(HttpInputLocation.Query, name));
    }

    /// <summary>Adds a header input.</summary>
    public HttpRequestSourceBuilder<T, T1, T2, T3> Header<T3>(string name)
    {
        parent.Inputs.Add(new(name, HttpInputLocation.Header, typeof(T3), true));
        return new(parent, first, second, read => read.Value<T3>(HttpInputLocation.Header, name));
    }

    /// <summary>Completes a two-input constructor.</summary>
    public HttpRequestBuilder<T> Map(Func<T1, T2, T> map)
    {
        parent.SetConstructor(async read => map(await first(read), await second(read)));
        return parent;
    }
}

/// <summary>Three explicitly typed inputs in a complete Map constructor.</summary>
public sealed class HttpRequestSourceBuilder<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] T, T1, T2, T3>(HttpRequestBuilder<T> parent, Func<HttpRequestReadContext, ValueTask<T1>> first, Func<HttpRequestReadContext, ValueTask<T2>> second, Func<HttpRequestReadContext, ValueTask<T3>> third)
{
    /// <summary>Completes a three-input constructor.</summary>
    public HttpRequestBuilder<T> Map(Func<T1, T2, T3, T> map)
    {
        parent.SetConstructor(async read => map(await first(read), await second(read), await third(read)));
        return parent;
    }
}

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

        if (type.IsEnum)
        {
            return JsonSerializer.SerializeToNode(Enum.Parse(type, value, true), json.GetRequiredTypeInfo(type));
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
