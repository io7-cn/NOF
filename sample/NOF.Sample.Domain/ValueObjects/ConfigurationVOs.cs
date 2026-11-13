using NOF.Domain;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NOF.Sample;

[ValueObjectLength(100, MinimumLength = 1)]
public readonly partial struct ConfigNodeName : IValueObject<string>
{
    public static bool IsValid(string input, out string? errorMessage)
    {
        errorMessage = string.IsNullOrWhiteSpace(input) ? "Node name cannot be blank." : null;
        return errorMessage is null;
    }
}

[ValueObjectLength(100, MinimumLength = 1)]
public readonly partial struct ConfigFileName : IValueObject<string>
{
    public static bool IsValid(string input, out string? errorMessage)
    {
        errorMessage = string.IsNullOrWhiteSpace(input) ? "File name cannot be blank." : null;
        return errorMessage is null;
    }
}

public readonly partial struct ConfigContent : IValueObject<string>
{
    public static bool IsValid(string input, out string? errorMessage)
    {
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            errorMessage = "Configuration content cannot be blank.";
            return false;
        }
        try
        {
            if (JsonNode.Parse(input) is JsonObject)
            {
                return true;
            }
            errorMessage = "Configuration content must be a JSON object.";
            return false;
        }
        catch (JsonException)
        {
            errorMessage = "Invalid JSON format.";
            return false;
        }
    }
}
