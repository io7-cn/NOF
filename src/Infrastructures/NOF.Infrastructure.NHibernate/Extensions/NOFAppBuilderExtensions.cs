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
            builder.Services.ReplaceOrAddScoped(sp =>
                sp.GetRequiredService<NHibernateSessionFactoryRegistry>()
                    .OpenSession(TenantId.Normalize(Context.Current.TenantId)));
            builder.Services.AddScoped<NHibernateUnitOfWork>();
            builder.Services.ReplaceOrAddScoped<IUnitOfWork>(sp => sp.GetRequiredService<NHibernateUnitOfWork>());
            builder.Services.ReplaceOrAddScoped<ITransactionManager>(sp => sp.GetRequiredService<NHibernateUnitOfWork>());
            builder.Services.Replace(ServiceDescriptor.KeyedScoped<IInboxStore, NHibernateInboxStore>(typeof(IInboxStore)));
            builder.Services.Replace(ServiceDescriptor.KeyedScoped<IOutboxStore, NHibernateOutboxStore>(typeof(IOutboxStore)));

            return new NHibernateSelector(builder);
        }
    }
}
