namespace NOF.Contract;

/// <summary>A completed application operation, whose business value may be null or a business failure.</summary>
/// <param name="Response">The operation's unchanged business value.</param>
public sealed record ServiceCompleted(object? Response);
