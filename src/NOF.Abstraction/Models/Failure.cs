using System.Text.Json;
using System.Text.Json.Serialization;

namespace NOF.Abstraction;

/// <summary>A shared business failure for domain rules and operation outcomes.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Failure
{
    /// <summary>Creates a failure with a stable code and user-facing message.</summary>
    [JsonConstructor]
    public Failure(string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(message);
        Code = code;
        Message = message;
    }

    /// <summary>Gets the reserved JSON discriminator identifying a failure case.</summary>
    [JsonPropertyName("$kind")]
    [JsonRequired]
    public string Kind
    {
        get => "failure";
        init
        {
            if (value != "failure")
            {
                throw new JsonException("A failure must have $kind 'failure'.");
            }
        }
    }

    /// <summary>Gets the application-specific failure code.</summary>
    public string Code { get; }

    /// <summary>Gets the user-facing failure message.</summary>
    public string Message { get; }

    /// <summary>Extracts the failure code and message.</summary>
    public void Deconstruct(out string code, out string message)
    {
        code = Code;
        message = Message;
    }
}
