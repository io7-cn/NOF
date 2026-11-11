namespace NOF.Contract;

/// <summary>Provides transport access to an already selected successful case.</summary>
public interface ISuccess
{
    /// <summary>Gets the successful user-facing message.</summary>
    string Message { get; }
}
