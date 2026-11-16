namespace NOF.Infrastructure;

/// <summary>Signals an infrastructure delivery failure for which retrying the same intent cannot succeed.</summary>
public sealed class OutboxPermanentFailureException(string message, Exception? innerException = null)
    : Exception(message, innerException);
