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

    /// <summary>Gets whether the built-in JSON reader owns the body and its 400/415 failures.</summary>
    public bool ReadsJsonBody { get; private set; }

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
        ReadsJsonBody = false;
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
        ReadsJsonBody = false;
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
        ReadsJsonBody = true;
    }

    private HttpRequestSourceBuilder<T, TValue> Source<TValue>(HttpInputLocation source, string name)
    {
        Inputs.Add(new(name, source, typeof(TValue), true, AllowsNull: Nullable.GetUnderlyingType(typeof(TValue)) is not null));
        return new(this, read => read.Value<TValue>(source, name));
    }
}
