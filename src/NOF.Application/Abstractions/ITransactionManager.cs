namespace NOF.Application;

/// <summary>Begins an explicit transaction shared by repositories and message staging in the current scope.</summary>
public interface ITransactionManager
{
    /// <summary>Begins the current scope's transaction.</summary>
    Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}
