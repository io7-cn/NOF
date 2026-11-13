using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NOF.Abstraction;
using System.Globalization;

namespace NOF.Hosting.AspNetCore;

/// <summary>One configured target and its declared value type.</summary>
public sealed record HttpValueProvider(Type ValueType, Func<object?, HttpOutputContext, object?> Read);

/// <summary>Mutable configuration captured as endpoint convention metadata before plan compilation.</summary>
public sealed class HttpResponseRule
{
    /// <summary>Gets the exact source or common default type.</summary>
    public required Type SourceType { get; init; }
    /// <summary>Gets an optional named conditional variant.</summary>
    public string? Variant { get; init; }
    /// <summary>Gets the filter owning this default consumer, if any.</summary>
    public Type? FilterType { get; init; }
    /// <summary>Gets whether the rule was explicitly attached to the concrete endpoint.</summary>
    public bool Endpoint { get; init; }
    /// <summary>Gets independent target slots; absent targets inherit.</summary>
    public Dictionary<string, HttpValueProvider> Targets { get; } = new(StringComparer.Ordinal);

    /// <summary>Writes a single target, comparing only HTTP header names without case.</summary>
    public void SetTarget(string name, HttpValueProvider value)
    {
        if (name.StartsWith("header:", StringComparison.Ordinal))
        {
            var previous = Targets.Keys.FirstOrDefault(key => key.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (previous is not null)
            {
                Targets.Remove(previous);
            }
        }

        Targets[name] = value;
    }

    /// <summary>Replaces the body slot and masks earlier field patches in this declaration.</summary>
    public void ReplaceBody()
    {
        Writer = null;
        WriterDescription = null;
        Targets.Remove("body");
        foreach (var name in Targets.Keys.Where(key => key.StartsWith("problem:", StringComparison.Ordinal)).ToArray())
        {
            Targets.Remove(name);
        }
    }

    /// <summary>Gets all possible values of the configured status provider.</summary>
    public IReadOnlyList<int>? StatusCodes { get; set; }
    /// <summary>Gets a custom native writer and its complete declared body contract.</summary>
    public Func<object?, HttpOutputContext, IResult>? Writer { get; set; }
    /// <summary>Gets custom native writer metadata.</summary>
    public HttpBodyDescription? WriterDescription { get; set; }
}

/// <summary>Configures typed response targets; every target merges independently.</summary>
public sealed class HttpResponseBuilder<T>(HttpResponseRule rule)
{
    /// <summary>Assigns a fixed HTTP status.</summary>
    public HttpResponseBuilder<T> StatusCode(int value) => StatusCode((_, _) => value, [value]);
    /// <summary>Assigns a dynamic status with a complete declared set.</summary>
    public HttpResponseBuilder<T> StatusCode(Func<T, int> selector, params int[] possibleValues) => StatusCode((value, _) => selector(value), possibleValues);
    /// <summary>Assigns a boundary-aware dynamic status with a complete declared set.</summary>
    public HttpResponseBuilder<T> StatusCode(Func<T, HttpOutputContext, int> selector, params int[] possibleValues)
    {
        if (possibleValues.Length == 0 || possibleValues.Any(value => value < 100 || value > 599))
        {
            throw new ArgumentException("Declare valid possible HTTP status codes.", nameof(possibleValues));
        }

        rule.StatusCodes = Array.AsReadOnly(possibleValues.Distinct().ToArray());
        return Set("status", selector);
    }

    /// <summary>Projects the response body.</summary>
    public HttpResponseBuilder<T> Body<TBody>(Func<T, TBody> selector) => Body((value, _) => selector(value));
    /// <summary>Projects the response body using the saved HTTP boundary.</summary>
    public HttpResponseBuilder<T> Body<TBody>(Func<T, HttpOutputContext, TBody> selector) => Set("body", selector);
    /// <summary>Creates a complete Problem object.</summary>
    public HttpResponseBuilder<T> Problem<TProblem>(Func<T, TProblem> selector)
        where TProblem : ProblemDetails => Body(selector);
    /// <summary>Creates a complete Problem object using the HTTP boundary.</summary>
    public HttpResponseBuilder<T> Problem<TProblem>(Func<T, HttpOutputContext, TProblem> selector)
        where TProblem : ProblemDetails => Body(selector);
    /// <summary>Explicitly removes the inherited body.</summary>
    public HttpResponseBuilder<T> NoBody()
    {
        rule.ReplaceBody();
        rule.SetTarget("body", new(typeof(void), (_, _) => null));
        return this;
    }

    /// <summary>Assigns a fixed header.</summary>
    public HttpResponseBuilder<T> Header<THeader>(string name, THeader value) => Header(name, (_, _) => value);
    /// <summary>Assigns a header from the source.</summary>
    public HttpResponseBuilder<T> Header<THeader>(string name, Func<T, THeader> selector) => Header(name, (value, _) => selector(value));
    /// <summary>Assigns a header using the HTTP boundary.</summary>
    public HttpResponseBuilder<T> Header<THeader>(string name, Func<T, HttpOutputContext, THeader> selector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Set("header:" + name, selector);
    }

    /// <summary>Removes one inherited header.</summary>
    public HttpResponseBuilder<T> RemoveHeader(string name)
    {
        rule.SetTarget("header:" + name, new(typeof(void), (_, _) => null));
        return this;
    }

    /// <summary>Patches individual Problem fields and extension keys.</summary>
    public HttpResponseBuilder<T> ConfigureProblem(Action<HttpProblemBuilder<T>> configure)
    {
        configure(new(rule));
        return this;
    }

    /// <summary>Uses a native protocol writer with explicit documentation; no function-body analysis occurs.</summary>
    public HttpResponseBuilder<T> Result(Func<T, HttpOutputContext, IResult> writer, HttpBodyDescription description)
    {
        rule.ReplaceBody();
        rule.Writer = (value, context) => writer((T)value!, context);
        rule.WriterDescription = description;
        return this;
    }

    private HttpResponseBuilder<T> Set<TValue>(string target, Func<T, HttpOutputContext, TValue> provider)
    {
        if (target == "body")
        {
            rule.ReplaceBody();
        }

        rule.SetTarget(target, new(typeof(TValue), (value, context) => provider((T)value!, context)));
        return this;
    }
}

/// <summary>Configures Problem fields without replacing unrelated inherited fields.</summary>
public sealed class HttpProblemBuilder<T>(HttpResponseRule rule)
{
    /// <summary>Sets a constant title.</summary>
    public HttpProblemBuilder<T> Title(string? value) => Title((_, _) => value);
    /// <summary>Projects a title.</summary>
    public HttpProblemBuilder<T> Title(Func<T, string?> value) => Title((source, _) => value(source));
    /// <summary>Projects a boundary-aware title.</summary>
    public HttpProblemBuilder<T> Title(Func<T, HttpOutputContext, string?> value) => Set("title", value);
    /// <summary>Sets constant detail.</summary>
    public HttpProblemBuilder<T> Detail(string? value) => Detail((_, _) => value);
    /// <summary>Projects detail.</summary>
    public HttpProblemBuilder<T> Detail(Func<T, string?> value) => Detail((source, _) => value(source));
    /// <summary>Projects boundary-aware detail.</summary>
    public HttpProblemBuilder<T> Detail(Func<T, HttpOutputContext, string?> value) => Set("detail", value);
    /// <summary>Sets a problem type URI.</summary>
    public HttpProblemBuilder<T> Type(string? value) => Type((_, _) => value);
    /// <summary>Projects a problem type URI.</summary>
    public HttpProblemBuilder<T> Type(Func<T, string?> value) => Type((source, _) => value(source));
    /// <summary>Projects a boundary-aware problem type URI.</summary>
    public HttpProblemBuilder<T> Type(Func<T, HttpOutputContext, string?> value) => Set("type", value);
    /// <summary>Sets a problem instance.</summary>
    public HttpProblemBuilder<T> Instance(string? value) => Instance((_, _) => value);
    /// <summary>Projects a problem instance.</summary>
    public HttpProblemBuilder<T> Instance(Func<T, string?> value) => Instance((source, _) => value(source));
    /// <summary>Projects a boundary-aware problem instance.</summary>
    public HttpProblemBuilder<T> Instance(Func<T, HttpOutputContext, string?> value) => Set("instance", value);
    /// <summary>Sets a typed extension.</summary>
    public HttpProblemBuilder<T> Extension<TValue>(string name, TValue value) => Extension(name, (_, _) => value);
    /// <summary>Projects a typed extension.</summary>
    public HttpProblemBuilder<T> Extension<TValue>(string name, Func<T, TValue> value) => Extension(name, (source, _) => value(source));
    /// <summary>Projects a typed extension using the boundary.</summary>
    public HttpProblemBuilder<T> Extension<TValue>(string name, Func<T, HttpOutputContext, TValue> value)
    {
        if (new[]
        {
            "type",
            "title",
            "detail",
            "status",
            "instance"
        }.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Extensions cannot replace standard Problem fields.", nameof(name));
        }

        return Set("extension:" + name, value);
    }

    private HttpProblemBuilder<T> Set<TValue>(string target, Func<T, HttpOutputContext, TValue> provider)
    {
        rule.SetTarget("problem:" + target, new(typeof(TValue), (value, context) => provider((T)value!, context)));
        return this;
    }
}

/// <summary>A validated symbolic reference to a final named mapping.</summary>
public sealed record HttpResponseReference(Type SourceType, string? Variant);

/// <summary>Selects symbolic response mappings without executing or inspecting output delegates.</summary>
public sealed class HttpResponseMappings
{
    /// <summary>Gets a named mapping for a source case.</summary>
    public HttpResponseReference Get<T>(string name) => new(typeof(T), name);
    /// <summary>Uses the default mapping for the actual source case.</summary>
    public HttpResponseReference Fallback(object? value) => new(value?.GetType() ?? typeof(void), null);
}
