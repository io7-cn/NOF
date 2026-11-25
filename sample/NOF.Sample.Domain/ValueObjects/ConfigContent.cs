using NOF.Domain;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NOF.Sample;

public readonly partial struct ConfigContent : IValueObject<string>
{
    public static bool IsValid(string input, [NotNullWhen(false)] out string? errorMessage)
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
