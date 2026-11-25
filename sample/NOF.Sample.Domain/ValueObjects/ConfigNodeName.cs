using NOF.Domain;
using System.Diagnostics.CodeAnalysis;

namespace NOF.Sample;

[ValueObjectLength(100, MinimumLength = 1)]
public readonly partial struct ConfigNodeName : IValueObject<string>
{
    public static bool IsValid(string input, [NotNullWhen(false)] out string? errorMessage)
    {
        errorMessage = string.IsNullOrWhiteSpace(input) ? "Node name cannot be blank." : null;
        return errorMessage is null;
    }
}
