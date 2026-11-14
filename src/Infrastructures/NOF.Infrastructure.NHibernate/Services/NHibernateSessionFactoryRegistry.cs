using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NHibernate;
using NHibernate.Cfg;
using NHibernate.Mapping.ByCode;
using NHibernate.Tool.hbm2ddl;
using System.Collections.Concurrent;
using System.Data;
using System.Reflection;

namespace NOF.Infrastructure.NHibernate;
/// <summary>Maintains NHibernate session factories for tenant connections.</summary>

public sealed class NHibernateSessionFactoryRegistry : IDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<ISessionFactory>> SessionFactories = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> SchemaLocks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> InitializedSchemas = new(StringComparer.Ordinal);
    private readonly NHibernateConfigurationOptions _options;
    private readonly IEnumerable<Action<ModelMapper>> _configurations;
    private readonly Microsoft.Extensions.DependencyInjection.IServiceProviderIsService _capabilities;
    private readonly ILogger<NHibernateSessionFactoryRegistry> _logger;

    public NHibernateSessionFactoryRegistry(
        IOptions<NHibernateConfigurationOptions> options,
        IEnumerable<Action<ModelMapper>> configurations,
        Microsoft.Extensions.DependencyInjection.IServiceProviderIsService capabilities,
        ILogger<NHibernateSessionFactoryRegistry> logger)
    {
        _options = options.Value;
        _configurations = configurations;
        _capabilities = capabilities;
        _logger = logger;
    }

    public ISession OpenSession(string tenantId)
    {
        tenantId = TenantId.Normalize(tenantId);
        var key = GetFactoryKey(tenantId);
        var sessionFactory = SessionFactories.GetOrAdd(
            key,
            _ => new Lazy<ISessionFactory>(() => BuildSessionFactory(tenantId), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

        return sessionFactory.OpenSession();
    }

    private ISessionFactory BuildSessionFactory(string tenantId)
    {
        if (_options.TenantMode != TenantMode.DatabasePerTenant)
        {
            throw new NotSupportedException("NOF.Infrastructure.NHibernate currently supports only TenantMode.DatabasePerTenant.");
        }

        var connectionString = DbConnectionStringTemplateResolver.ResolveTenantId(_options.ConnectionStringTemplate, tenantId);
        var configuration = new Configuration();

        configuration.DataBaseIntegration(db =>
        {
            db.ConnectionString = connectionString;
            db.IsolationLevel = IsolationLevel.ReadCommitted;
        });

        _options.Configure(configuration, connectionString);

        var mapper = new ModelMapper();
        if (_capabilities.IsService(typeof(IInboxStore)))
        { MessageModelConfigurations.Inbox(mapper); }
        if (_capabilities.IsService(typeof(IOutboxStore)))
        { MessageModelConfigurations.Outbox(mapper); }
        foreach (var configure in _configurations)
        { configure(mapper); }

        configuration.AddMapping(mapper.CompileMappingForAllExplicitlyAddedEntities());
        EnsureSchemaInitialized(configuration, connectionString);

        var sessionFactory = configuration.BuildSessionFactory();
        _logger.LogDebug("Created NHibernate session factory for tenant '{TenantId}'.", tenantId);
        return sessionFactory;
    }

    private void EnsureSchemaInitialized(Configuration configuration, string connectionString)
    {
        if (!_options.BuildSchemaOnInitialize)
        {
            return;
        }

        var key = connectionString;
        if (InitializedSchemas.ContainsKey(key))
        {
            return;
        }

        var schemaLock = SchemaLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        schemaLock.Wait();
        try
        {
            if (InitializedSchemas.ContainsKey(key))
            {
                return;
            }

            new SchemaUpdate(configuration).Execute(false, true);
            InitializedSchemas.TryAdd(key, 0);
        }
        finally
        {
            schemaLock.Release();
        }
    }

    private string GetFactoryKey(string tenantId)
        => $"{_options.TenantMode}|{DbConnectionStringTemplateResolver.ResolveTenantId(_options.ConnectionStringTemplate, tenantId)}";

    /// <summary>Releases the native factories owned by this host.</summary>
    public void Dispose()
    {
        foreach (var factory in SessionFactories.Values)
        {
            if (factory.IsValueCreated)
            { factory.Value.Dispose(); }
        }
    }
}
