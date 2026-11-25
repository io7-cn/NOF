using NOF.Domain;
using System.Diagnostics.CodeAnalysis;

namespace NOF.Sample;

[ValueObjectLength(100, MinimumLength = 1)]
public readonly partial struct ConfigFileName : IValueObject<string>
{
    public static bool IsValid(string input, [NotNullWhen(false)] out string? errorMessage)
    {
        errorMessage = string.IsNullOrWhiteSpace(input) ? "File name cannot be blank." : null;
        return errorMessage is null;
    }
}
