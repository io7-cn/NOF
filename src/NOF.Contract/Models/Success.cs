using System.Text.Json;
using System.Text.Json.Serialization;

namespace NOF.Contract;

/// <summary>A successful operation with no payload and an optional message.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Success : ISuccess
{
    /// <summary>Creates a successful operation.</summary>
    [JsonConstructor]
    public Success(string message = "", string kind = "success")
    {
        ArgumentNullException.ThrowIfNull(message);
        if (kind != "success")
        {
            throw new JsonException("A success must have kind 'success'.");
        }

        Message = message;
    }

    /// <summary>Gets the required wire discriminator.</summary>
    [JsonRequired]
    public string Kind
    {
        get => "success";
        init
        {
            if (value != "success")
            {
                throw new JsonException("Invalid outcome discriminator.");
            }
        }
    }

    /// <summary>Gets the user-facing successful message.</summary>
    public string Message { get; }

    /// <summary>Extracts the successful message.</summary>
    public void Deconstruct(out string message) => message = Message;
}
