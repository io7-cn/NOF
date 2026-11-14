using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NOF.Application;
using NOF.Infrastructure;
using NOF.Infrastructure.EntityFrameworkCore;
using System.Diagnostics.CodeAnalysis;

namespace NOF.Hosting;

public static partial class NOFInfrastructureExtensions
{
    extension(IHostApplicationBuilder builder)
    {
        public IHostApplicationBuilder AddNOFEntityFrameworkCore()
        {
            builder.Services.AddOptions<DbContextConfigurationOptions>();
            builder.Services.TryAddSingleton<SqliteInMemoryConnectionKeeper>();

            builder.UseDbContext<NOFDbContext>()
                .WithConnectionString("Data Source=nof-sqlite-memory-{tenantId};Mode=Memory;Cache=Shared")
                .WithTenantMode(TenantMode.DatabasePerTenant)
                .WithOptions(static (optionsBuilder, connectionString) => optionsBuilder.UseSqlite(connectionString));

            return builder;
        }

        public EFCoreSelector UseDbContext<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)] TDbContext>()
            where TDbContext : NOFDbContext
        {
            builder.Services.AddOptions<DbContextConfigurationOptions>();
            builder.Services.TryAddSingleton<SqliteInMemoryConnectionKeeper>();
            builder.Services.Replace(ServiceDescriptor.Scoped<NOFDbContextFactory<TDbContext>, NOFDbContextFactory<TDbContext>>());
            builder.Services.Replace(ServiceDescriptor.Scoped<ITenantDbContextFactory<TDbContext>>(sp =>
                sp.GetRequiredService<NOFDbContextFactory<TDbContext>>()));
            builder.Services.Replace(ServiceDescriptor.Scoped<IDbContextFactory<TDbContext>>(sp =>
                sp.GetRequiredService<ITenantDbContextFactory<TDbContext>>()));
            builder.Services.Replace(ServiceDescriptor.Scoped<NOFDbContext>(sp => sp.GetRequiredService<NOFDbContextFactory<TDbContext>>().CreateDbContext()));
            builder.Services.Replace(ServiceDescriptor.Scoped<DbContext>(sp => sp.GetRequiredService<NOFDbContext>()));
            builder.Services.AddScoped<EfCoreUnitOfWork>();
            builder.Services.Replace(ServiceDescriptor.Scoped<IUnitOfWork>(sp => sp.GetRequiredService<EfCoreUnitOfWork>()));
            builder.Services.Replace(ServiceDescriptor.Scoped<ITransactionManager>(sp => sp.GetRequiredService<EfCoreUnitOfWork>()));
            builder.Services.Replace(ServiceDescriptor.KeyedScoped<IInboxStore, EfCoreInboxStore>(typeof(IInboxStore)));
            builder.Services.Replace(ServiceDescriptor.KeyedScoped<IOutboxStore, EfCoreOutboxStore>(typeof(IOutboxStore)));
            if (typeof(TDbContext) != typeof(NOFDbContext))
            {
                builder.Services.Replace(ServiceDescriptor.Scoped(sp => (TDbContext)sp.GetRequiredService<NOFDbContext>()));
            }

            return new EFCoreSelector(builder, typeof(TDbContext));
        }

        public EFCoreSelector UseDbContext<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)] TDbContext,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TDbContextFactory>()
            where TDbContext : NOFDbContext
            where TDbContextFactory : NOFDbContextFactory<TDbContext>
        {
            var selector = builder.UseDbContext<TDbContext>();
            if (typeof(TDbContextFactory) == typeof(NOFDbContextFactory<TDbContext>))
            {
                return selector;
            }

            builder.Services.Replace(ServiceDescriptor.Scoped<TDbContextFactory, TDbContextFactory>());
            builder.Services.Replace(ServiceDescriptor.Scoped<NOFDbContextFactory<TDbContext>>(sp =>
                sp.GetRequiredService<TDbContextFactory>()));
            return selector;
        }

    }
}
