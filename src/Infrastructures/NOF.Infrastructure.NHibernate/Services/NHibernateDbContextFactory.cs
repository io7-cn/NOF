using NOF.Application;
using NOF.Contract;
using NOF.Infrastructure;

namespace NOF.Infrastructure.NHibernate;
/// <summary>Creates database contexts backed by tenant-aware NHibernate sessions.</summary>

public sealed class NHibernateDbContextFactory(
    NHibernateSessionFactoryRegistry sessionFactoryRegistry) : IPersistenceSessionFactory
{
    public IPersistenceSession CreateDbContext()
        => new NHibernateDbContextAdapter(sessionFactoryRegistry.OpenSession(TenantId.Normalize(Context.Current.TenantId)));

    public IPersistenceSession CreateDbContext(string tenantId)
        => new NHibernateDbContextAdapter(sessionFactoryRegistry.OpenSession(TenantId.Normalize(tenantId)));
}
