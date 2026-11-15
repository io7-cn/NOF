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
            builder.Services.AddOptions<NOFDbContextOptions>();
            builder.Services.TryAddSingleton<SqliteInMemoryConnectionKeeper>();

            builder.UseDbContext<NOFDbContext>(static (creation, ef) =>
                ef.UseSqlite($"Data Source=nof-sqlite-memory-{creation.TenantId};Mode=Memory;Cache=Shared"));

            return builder;
        }

        /// <summary>Registers NOF persistence integration with native typed EF configuration run for every context creation.</summary>
        public EFCoreSelector UseDbContext<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)] TDbContext>(
            Action<DbContextCreationContext, DbContextOptionsBuilder<TDbContext>>? configure = null)
            where TDbContext : NOFDbContext
        {
            builder.Services.AddOptions<NOFDbContextOptions>();
            if (configure is not null)
            {
                builder.Services.AddSingleton(configure);
            }
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

        /// <summary>Registers a custom tenant factory while retaining native typed EF configuration.</summary>
        public EFCoreSelector UseDbContext<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)] TDbContext,
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TDbContextFactory>(
            Action<DbContextCreationContext, DbContextOptionsBuilder<TDbContext>>? configure = null)
            where TDbContext : NOFDbContext
            where TDbContextFactory : NOFDbContextFactory<TDbContext>
        {
            var selector = builder.UseDbContext<TDbContext>(configure);
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
