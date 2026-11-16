using System.Text.Json.Serialization;

namespace NOF.Contract;

/// <summary>
/// Represents an explicit empty payload for contracts that do not need to return data.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class Empty
{
    /// <summary>Gets the shared empty payload.</summary>
    public static readonly Empty Instance = new();
}
