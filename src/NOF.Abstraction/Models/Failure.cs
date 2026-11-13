using System.Text.Json;
using System.Text.Json.Serialization;

namespace NOF.Abstraction;

/// <summary>A transport-independent predictable failure.</summary>
public interface IFailure
{
    /// <summary>Gets the failure summary.</summary>
    string Title { get; }
    /// <summary>Gets occurrence-specific details.</summary>
    string Detail { get; }
}

/// <summary>A predictable failure carrying typed business data.</summary>
public interface IFailure<out TError> : IFailure
{
    /// <summary>Gets the failure data.</summary>
    TError Error { get; }
}

/// <summary>A shared failure with a summary and occurrence-specific detail.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Failure : IFailure
{
    /// <summary>Creates a predictable failure.</summary>
    [JsonConstructor]
    public Failure(string title, string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(detail);
        Title = title;
        Detail = detail;
    }
    /// <summary>Gets the optional union JSON discriminator.</summary>
    [JsonPropertyName("$kind")]
    [JsonRequired]
    public string Kind
    {
        get => "failure";
        init { if (value != "failure") { throw new JsonException("A failure must have $kind 'failure'."); } }
    }
    /// <inheritdoc />
    public string Title { get; }
    /// <inheritdoc />
    public string Detail { get; }
    /// <summary>Extracts the summary and detail.</summary>
    public void Deconstruct(out string title, out string detail) { title = Title; detail = Detail; }
}
