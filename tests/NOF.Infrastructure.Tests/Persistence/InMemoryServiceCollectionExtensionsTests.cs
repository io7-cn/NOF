using Microsoft.Extensions.DependencyInjection;
using NOF.Application;
using NOF.Domain;
using NOF.Infrastructure;
using Xunit;

namespace NOF.Infrastructure.Tests.Persistence;

public sealed class InMemoryServiceCollectionExtensionsTests
{
    [Fact]
    public void AddInMemoryPersistence_OnServices_ShouldRegisterInMemoryPersistenceServices()
    {
        var services = new ServiceCollection();

        services.AddInMemoryPersistence();

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<InMemoryPersistenceStore>());
        Assert.Equal("InMemoryDbContext", scope.ServiceProvider.GetRequiredService<IPersistenceSession>().GetType().Name);
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPersistenceSession>().Set<RepositoryTestEntity>());
    }

    [Fact]
    public async Task AddInMemoryPersistence_ShouldExposeUnitOfWorkWithoutGenericRepositoryRegistration()
    {
        var services = new ServiceCollection();

        services.AddInMemoryPersistence();

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IPersistenceSession>().Set<RepositoryTestEntity>();
        repository.Add(new RepositoryTestEntity { Id = 1 });

        await scope.ServiceProvider.GetRequiredService<NOF.Application.IUnitOfWork>().SaveChangesAsync();

        Assert.True(scope.ServiceProvider.GetRequiredService<IPersistenceSession>().Set<RepositoryTestEntity>().Any(entity => entity.Id == 1));
        Assert.Null(scope.ServiceProvider.GetService<IEntitySet<RepositoryTestEntity>>());
    }

    [Fact]
    public void AddInMemoryPersistence_MultipleCalls_ShouldRemainIdempotent()
    {
        var services = new ServiceCollection();

        services.AddInMemoryPersistence();
        services.AddInMemoryPersistence();

        Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(InMemoryPersistenceStore));
        Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(IPersistenceSession));
        Assert.DoesNotContain(services, static descriptor => descriptor.ServiceType == typeof(IEntitySet<>));
        Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(IUnitOfWork));
    }

    private sealed class RepositoryTestEntity
    {
        public int Id { get; set; }
    }
}
