using System.Text.Json.Serialization;

namespace NOF.Contract;

/// <summary>A successful operation carrying non-null data and an optional message.</summary>
/// <typeparam name="T">The payload type.</typeparam>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Success<T>
{
    /// <summary>Creates optional successful metadata with non-null data.</summary>
    [JsonConstructor]
    public Success(T data, string message = "")
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(message);
        Data = data;
        Message = message;
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
