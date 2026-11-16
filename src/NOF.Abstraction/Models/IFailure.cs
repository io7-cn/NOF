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
