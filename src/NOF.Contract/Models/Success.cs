using System.Text.Json.Serialization;

namespace NOF.Contract;

/// <summary>A successful operation with no payload and an optional message.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Success
{
    /// <summary>Creates a successful operation.</summary>
    [JsonConstructor]
    public Success(string message = "")
    {
        ArgumentNullException.ThrowIfNull(message);
        Message = message;
    }

    /// <summary>Gets the user-facing successful message.</summary>
    public string Message { get; }

    /// <summary>Extracts the successful message.</summary>
    public void Deconstruct(out string message) => message = Message;
}
