using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NHibernate;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using NOF.Infrastructure;

namespace NOF.Infrastructure.NHibernate;

public static partial class NOFInfrastructureExtensions
{
    extension(IHostApplicationBuilder builder)
    {
        public NHibernateSelector UseNHibernate()
        {
            builder.Services.AddOptions<NHibernateConfigurationOptions>();
            builder.Services.TryAddSingleton<NHibernateSessionFactoryRegistry>();
            builder.Services.ReplaceOrAddScoped<IPersistenceSessionFactory, NHibernateDbContextFactory>();
            builder.Services.ReplaceOrAddScoped(sp =>
                sp.GetRequiredService<NHibernateSessionFactoryRegistry>()
                    .OpenSession(TenantId.Normalize(Context.Current.TenantId)));
            builder.Services.ReplaceOrAddScoped<IPersistenceSession>(sp =>
                new NHibernateDbContextAdapter(sp.GetRequiredService<ISession>()));
            builder.Services.AddPersistenceServices();

            return new NHibernateSelector(builder);
        }
    }
}
