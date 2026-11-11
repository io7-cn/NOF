using System.Text.Json;
using System.Text.Json.Serialization;

namespace NOF.Contract;

/// <summary>A business failure with a stable error code and a user-facing message.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Failure
{
    /// <summary>Creates a business failure.</summary>
    public Failure(string code, string message) : this(new ResultError(code, message)) { }

    /// <summary>Creates a failure from its wire error object.</summary>
    [JsonConstructor]
    public Failure(ResultError error, string kind = "failure")
    {
        ArgumentNullException.ThrowIfNull(error);
        if (kind != "failure")
        {
            throw new JsonException("A failure must have kind 'failure'.");
        }

        Error = error;
    }

    /// <summary>Gets the required wire discriminator.</summary>
    [JsonRequired]
    public string Kind
    {
        get => "failure";
        init
        {
            if (value != "failure")
            {
                throw new JsonException("Invalid outcome discriminator.");
            }
        }
    }

    /// <summary>Gets the required wire error details.</summary>
    [JsonRequired]
    public ResultError Error
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    }

    /// <summary>Gets the stable error code.</summary>
    [JsonIgnore]
    public string Code => Error.Code;

    /// <summary>Gets the user-facing failure message.</summary>
    [JsonIgnore]
    public string Message => Error.Message;

    /// <summary>Extracts the error code and message.</summary>
    public void Deconstruct(out string code, out string message)
    {
        code = Code;
        message = Message;
    }
}
