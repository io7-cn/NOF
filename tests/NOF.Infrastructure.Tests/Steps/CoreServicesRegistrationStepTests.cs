using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using NOF.Abstraction;
using NOF.Application;
using NOF.Domain;
using NOF.Hosting;
using NOF.Infrastructure;
using NOF.Infrastructure.EntityFrameworkCore;
using System.Diagnostics;
using Xunit;

namespace NOF.Infrastructure.Tests.Steps;

public class NOFInfrastructureTests
{
    [Fact]
    public void AddNOF_ShouldRegisterInMemoryPersistenceByDefault()
    {
        var builder = new TestServiceRegistrationContext();
        builder.Services.AddSingleton<IIdGenerator>(new TestIdGenerator());
        builder.Services.AddSingleton<IConfiguration>(builder.Configuration);

        builder.AddNOF();

        using var provider = BuildServiceProvider(builder);
        using var scope = provider.CreateScope();
        Assert.Null(scope.ServiceProvider.GetService<Microsoft.EntityFrameworkCore.DbContext>());
        Assert.Equal("InMemoryDbContext", scope.ServiceProvider.GetRequiredService<IPersistenceSession>().GetType().Name);
        Assert.IsType<CacheService>(scope.ServiceProvider.GetRequiredService<ICacheService>());
        Assert.IsType<MemoryCacheServiceRider>(scope.ServiceProvider.GetRequiredService<ICacheServiceRider>());
        Assert.IsType<ObjectStorageService>(scope.ServiceProvider.GetRequiredService<IObjectStorage>());
        Assert.IsType<FileSystemObjectStorageRider>(scope.ServiceProvider.GetRequiredService<IObjectStorageRider>());
    }

    [Fact]
    public void AddNOF_ShouldRegisterCoreServicesAndOutboxOptions()
    {
        var builder = new TestServiceRegistrationContext();
        builder.AddNOF();

        using var provider = BuildServiceProvider(builder);

        Assert.Contains(builder.Services, service =>
            service.ServiceType == typeof(IHostedService) &&
            service.ImplementationType == typeof(OutboxMessageBackgroundService));
        Assert.Contains(builder.Services, service =>
            service.ServiceType == typeof(IHostedService) &&
            service.ImplementationType == typeof(InboxMessageBackgroundService));
        Assert.Contains(builder.Services, service =>
            service.ServiceType == typeof(IHostedService) &&
            service.ImplementationType == typeof(InboxCleanupBackgroundService));
        Assert.Contains(builder.Services, service =>
            service.ServiceType == typeof(IHostedService) &&
            service.ImplementationType == typeof(OutboxCleanupBackgroundService));
        Assert.NotNull(provider.GetRequiredService<IOptions<TransactionalMessageOptions>>());
        Assert.Equal(TenantMode.DatabasePerTenant, provider.GetRequiredService<IOptions<DbContextConfigurationOptions>>().Value.TenantMode);
        Assert.IsType<MemoryBackplane>(provider.GetRequiredService<IBackplane>());
        Assert.IsType<InMemoryEventPublisher>(provider.GetRequiredService<IEventPublisher>());
        Assert.IsType<HttpAuthorizationServerService>(provider.GetRequiredService<HttpAuthorizationServerService>());
    }

    [Fact]
    public void AddNOF_ShouldRegisterSupportedAmbientDaemons()
    {
        var builder = new TestServiceRegistrationContext();

        builder.AddNOF();

        Assert.Contains(builder.Services, service =>
            service.ServiceType == typeof(IDaemonService) &&
            service.ImplementationType == typeof(MapperAmbientDaemonService));
        Assert.Contains(builder.Services, service =>
            service.ServiceType == typeof(IDaemonService) &&
            service.ImplementationType == typeof(IdGeneratorAmbientDaemonService));
    }

    [Fact]
    public void AddHostedService_WithDelegate_ShouldRegisterDelegateBackgroundService()
    {
        var services = new ServiceCollection();

        services.AddHostedService((_, _) => Task.CompletedTask);

        var descriptor = Assert.Single(services, service => service.ServiceType == typeof(IHostedService));
        Assert.NotNull(descriptor.ImplementationFactory);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<DelegateBackgroundService>(provider.GetRequiredService<IEnumerable<IHostedService>>().Single());
    }

    [Fact]
    public void UseDbContext_WithTenantMode_ShouldSwitchTenantMode()
    {
        var builder = new TestServiceRegistrationContext();
        builder.AddNOF();
        builder.UseDbContext<NOFDbContext>().WithTenantMode(TenantMode.SharedDatabase);

        using var provider = BuildServiceProvider(builder);
        var options = provider.GetRequiredService<IOptions<DbContextConfigurationOptions>>().Value;
        Assert.Equal(TenantMode.SharedDatabase, options.TenantMode);
    }

    [Fact]
    public void AddNOF_ShouldUseDatabasePerTenantByDefault()
    {
        var builder = new TestServiceRegistrationContext();
        builder.AddNOF();

        using var provider = BuildServiceProvider(builder);
        var options = provider.GetRequiredService<IOptions<DbContextConfigurationOptions>>().Value;
        Assert.Equal(TenantMode.DatabasePerTenant, options.TenantMode);
    }

    [Fact]
    public void AddNOF_ShouldDisableSoftDeleteByDefault()
    {
        var builder = new TestServiceRegistrationContext();
        builder.AddNOF();

        using var provider = BuildServiceProvider(builder);
        var options = provider.GetRequiredService<IOptions<DbContextConfigurationOptions>>().Value;
        Assert.False(options.SoftDeleteEnabled);
    }

    [Fact]
    public void UseDbContext_WithSoftDelete_ShouldEnableSoftDelete()
    {
        var builder = new TestServiceRegistrationContext();
        builder.AddNOF();
        builder.UseDbContext<NOFDbContext>().WithSoftDelete(true);

        using var provider = BuildServiceProvider(builder);
        var options = provider.GetRequiredService<IOptions<DbContextConfigurationOptions>>().Value;
        Assert.True(options.SoftDeleteEnabled);
    }

    [Fact]
    public void UseDbContext_MigrateOnStart_ShouldRegisterDatabaseMigrationStep()
    {
        var builder = new TestServiceRegistrationContext();

        builder.UseDbContext<NOFDbContext>().MigrateOnStart();

        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(DbContextMigration)
                && descriptor.ImplementationInstance is DbContextMigration);
    }

    [Fact]
    public void UseDbContext_ShouldRegisterTenantAwareTypedFactory()
    {
        var builder = new TestServiceRegistrationContext();
        builder.AddNOF();
        builder.UseDbContext<NOFDbContext>();

        using var provider = BuildServiceProvider(builder);
        using var scope = provider.CreateScope();

        var tenantFactory = scope.ServiceProvider.GetRequiredService<ITenantDbContextFactory<NOFDbContext>>();
        var efFactory = scope.ServiceProvider.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<NOFDbContext>>();

        Assert.Same(tenantFactory, efFactory);
    }

    [Fact]
    public void UseDbContext_WithCustomFactory_ShouldDispatchThroughOverriddenFactory()
    {
        var builder = new TestServiceRegistrationContext();
        builder.AddNOF();
        builder.UseDbContext<NOFDbContext, OverriddenDbContextFactory>();

        using var provider = BuildServiceProvider(builder);
        using var scope = provider.CreateScope();

        var customFactory = scope.ServiceProvider.GetRequiredService<OverriddenDbContextFactory>();
        var baseFactory = scope.ServiceProvider.GetRequiredService<NOFDbContextFactory<NOFDbContext>>();
        var tenantFactory = scope.ServiceProvider.GetRequiredService<ITenantDbContextFactory<NOFDbContext>>();
        var efFactory = scope.ServiceProvider.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<NOFDbContext>>();
        var nofFactory = scope.ServiceProvider.GetRequiredService<NOF.Infrastructure.IPersistenceSessionFactory>();

        Assert.Same(customFactory, baseFactory);
        Assert.Same(customFactory, tenantFactory);
        Assert.Same(customFactory, efFactory);
        Assert.Same(customFactory, nofFactory);

        using var dbContext = tenantFactory.CreateDbContext("tenant-a");
        Assert.Equal("tenant-a", customFactory.LastTenantId);
    }

    [Fact]
    public void AddNOF_ShouldValidateSnowflakeOptions()
    {
        var builder = new TestServiceRegistrationContext();
        builder.Services.Configure<SnowflakeIdGeneratorOptions>(options => options.ApplicationIdBits = 0);
        builder.AddNOF();

        using var provider = BuildServiceProvider(builder);
        var act = () => _ = provider.GetRequiredService<IOptions<SnowflakeIdGeneratorOptions>>().Value;
        Assert.Throws<OptionsValidationException>(act);
    }

    [Fact]
    public void AddNOF_ShouldConfigureDeploymentWithoutReplacingEnvironment()
    {
        var builder = new TestServiceRegistrationContext();
        var environment = builder.Environment;
        builder.Configuration[NOFHostingConstants.Deployment.ConfigurationKeys.ServiceName] = "Orders.Api";
        builder.Configuration[NOFHostingConstants.Deployment.ConfigurationKeys.ServiceId] = "42";
        builder.Configuration[NOFHostingConstants.Deployment.ConfigurationKeys.InstanceId] = "24";
        builder.AddNOF();
        using var provider = BuildServiceProvider(builder);
        var deployment = provider.GetRequiredService<IOptions<DeploymentOptions>>().Value;
        Assert.Same(environment, builder.Environment);
        Assert.Equal("Orders.Api", deployment.ServiceName);
        Assert.Equal(42u, deployment.ServiceId);
        Assert.Equal(24u, deployment.InstanceId);
        Assert.False(deployment.IsPrimaryNode);
    }

    [Fact]
    public void SetCurrentServiceDeploymentTags_ShouldAttachDeploymentInfoToActivity()
    {
        var hostEnvironment = new DeploymentOptions();
        hostEnvironment.ServiceId = 42;
        hostEnvironment.ServiceName = "orders-api";
        hostEnvironment.InstanceId = 24;

        using var activity = new Activity("deployment-test").Start();
        activity.SetServiceDeploymentTags(hostEnvironment);

        Assert.Equal(42u, activity.GetTagItem(NOFInfrastructureConstants.Deployment.Tags.ServiceId));
        Assert.Equal("orders-api", activity.GetTagItem(NOFInfrastructureConstants.Deployment.Tags.ServiceName));
        Assert.Equal(24u, activity.GetTagItem(NOFInfrastructureConstants.Deployment.Tags.InstanceId));
    }

    private sealed class TestServiceRegistrationContext : IHostApplicationBuilder
    {
        private readonly IServiceCollection _services;
        private readonly ConfigurationManager _configuration;
        private readonly IHostEnvironment _environment;
        private readonly ILoggingBuilder _logging;
        private readonly IMetricsBuilder _metrics;
        private readonly Dictionary<object, object> _properties;
        public TestServiceRegistrationContext()
        {
            _services = new ServiceCollection();
            _services.AddLogging();
            _services.AddMetrics();
            _configuration = new ConfigurationManager();
            _environment = new TestHostEnvironment();
            _logging = new TestLoggingBuilder(_services);
            _metrics = new TestMetricsBuilder(_services);
            _properties = [];
        }

        public TestServiceRegistrationContext(TestServiceRegistrationContext other)
        {
            _services = other._services;
            _configuration = other._configuration;
            _environment = other._environment;
            _logging = other._logging;
            _metrics = other._metrics;
            _properties = other._properties;
        }

        public IDictionary<object, object> Properties => _properties;

        public IConfigurationManager Configuration => _configuration;

        public IHostEnvironment Environment => _environment;

        public ILoggingBuilder Logging => _logging;

        public IMetricsBuilder Metrics => _metrics;

        public IServiceCollection Services => _services;

        public void ConfigureContainer<TContainerBuilder>(IServiceProviderFactory<TContainerBuilder> factory, Action<TContainerBuilder>? configure = null)
            where TContainerBuilder : notnull
        {
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "NOF.Infrastructure.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TestLoggingBuilder(IServiceCollection services) : ILoggingBuilder
    {
        public IServiceCollection Services { get; } = services;
    }

    private sealed class TestMetricsBuilder(IServiceCollection services) : IMetricsBuilder
    {
        public IServiceCollection Services { get; } = services;
    }

    private sealed class NullFileProvider : IFileProvider
    {
        public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;

        public IFileInfo GetFileInfo(string subpath) => new NotFoundFileInfo(subpath);

        public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
    }

    private sealed class TestIdGenerator : IIdGenerator
    {
        private long _current = 1000;

        public long NextId() => Interlocked.Increment(ref _current);
    }

    private sealed class OverriddenDbContextFactory : NOFDbContextFactory<NOFDbContext>
    {
        public OverriddenDbContextFactory(IServiceProvider serviceProvider)
            : base(serviceProvider)
        {
        }

        public string? LastTenantId { get; private set; }

        public override NOFDbContext CreateDbContext(string tenantId)
        {
            LastTenantId = tenantId;
            return new NOFDbContext(
                new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<NOFDbContext>().Options);
        }
    }

    private static ServiceProvider BuildServiceProvider(TestServiceRegistrationContext builder)
    {
        if (!builder.Services.Any(descriptor => descriptor.ServiceType == typeof(IHostEnvironment)))
        {
            builder.Services.AddSingleton(builder.Environment);
        }

        if (!builder.Services.Any(descriptor => descriptor.ServiceType == typeof(IConfiguration)))
        {
            builder.Services.AddSingleton<IConfiguration>(builder.Configuration);
        }

        return builder.Services.BuildServiceProvider();
    }

}
