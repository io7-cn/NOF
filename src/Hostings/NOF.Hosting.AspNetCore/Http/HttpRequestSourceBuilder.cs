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
