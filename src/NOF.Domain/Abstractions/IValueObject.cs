namespace NOF.Domain;

/// <summary>Declares the normalization and validity rules of a generated value object.</summary>
/// <typeparam name="T">The non-null primitive type.</typeparam>
/// <remarks>
/// Implement on a readonly partial struct. The generator supplies From, TryFrom,
/// primitive conversion, equality, initialization checks and JSON conversion.
/// From throws ArgumentException for invalid values; TryFrom returns false and
/// leaves its out parameter at default. Exceptions from user hooks propagate unchanged.
/// Validation receives the normalized value; generated length constraints run first.
/// Construct values through generated factories rather than default or new().
/// </remarks>
public interface IValueObject<T> where T : notnull
{
    /// <summary>Normalizes input before checking generated constraints and IsValid.</summary>
    /// <param name="value">The non-null input value.</param>
    /// <returns>The normalized value.</returns>
    static virtual T Normalize(T value) => value;

    /// <summary>Determines whether the normalized value satisfies custom business rules.</summary>
    /// <param name="value">The normalized value, already checked against generated constraints.</param>
    /// <param name="errorMessage">An optional explanation when invalid; null on success.</param>
    /// <returns>True if the value is valid; otherwise false.</returns>
    /// <remarks>Return false for expected invalid input. Hook exceptions are not caught by TryFrom.</remarks>
    static virtual bool IsValid(T value, out string? errorMessage)
    {
        errorMessage = null;
        return true;
    }
}
