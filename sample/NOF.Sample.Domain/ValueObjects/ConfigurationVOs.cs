using NOF.Domain;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NOF.Sample;

[ValueObjectLength(100, MinimumLength = 1)]
public readonly partial struct ConfigNodeName : IValueObject<string>
{
    public static bool IsValid(string input) => !string.IsNullOrWhiteSpace(input);
}

[ValueObjectLength(100, MinimumLength = 1)]
public readonly partial struct ConfigFileName : IValueObject<string>
{
    public static bool IsValid(string input) => !string.IsNullOrWhiteSpace(input);
}

public readonly partial struct ConfigContent : IValueObject<string>
{
    public static bool IsValid(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }
        try
        {
            return JsonNode.Parse(input) is JsonObject;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
