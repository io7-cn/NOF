using NOF.Infrastructure;
namespace NOF.Infrastructure;

/// <summary>
/// Creates provider-agnostic database contexts.
/// </summary>
public interface IPersistenceSessionFactory
{
    IPersistenceSession CreateDbContext();

    IPersistenceSession CreateDbContext(string tenantId);
}
