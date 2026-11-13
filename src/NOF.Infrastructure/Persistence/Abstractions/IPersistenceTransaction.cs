using NOF.Application;

namespace NOF.Infrastructure;

/// <summary>Provider transaction for infrastructure consumers; savepoints remain concrete provider capabilities.</summary>
public interface IPersistenceTransaction : ITransaction, IDisposable;
