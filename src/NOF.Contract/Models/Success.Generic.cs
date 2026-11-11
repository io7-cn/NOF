using System.Text.Json;
using System.Text.Json.Serialization;

namespace NOF.Contract;

/// <summary>A successful operation carrying non-null data and an optional message.</summary>
/// <typeparam name="T">The payload type.</typeparam>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Success<T> : ISuccess
{
    /// <summary>Creates a typed success and validates its data and discriminator.</summary>
    [JsonConstructor]
    public Success(T data, string message = "", string kind = "success")
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(message);
        if (kind != "success")
        {
            throw new JsonException("A success must have kind 'success'.");
        }

        Data = data;
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

    /// <summary>Gets the successful payload.</summary>
    [JsonRequired]
    public T Data
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    }

    /// <summary>Gets the user-facing successful message.</summary>
    public string Message { get; }

    /// <summary>Extracts the successful data and message.</summary>
    public void Deconstruct(out T data, out string message)
    {
        data = Data;
        message = Message;
    }
}
