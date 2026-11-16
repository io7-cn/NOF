using NHibernate.Cfg;

namespace NOF.Infrastructure.NHibernate;

public enum TenantMode
{
    SharedDatabase = 0,
    DatabasePerTenant = 1
}
