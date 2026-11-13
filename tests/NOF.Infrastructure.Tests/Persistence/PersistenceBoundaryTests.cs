using NOF.Application;
using NOF.Domain;
using Xunit;

namespace NOF.Infrastructure.Tests.Persistence;

public sealed class PersistenceBoundaryTests
{
    [Fact]
    public void CoreAssemblies_DoNotExposeAnOrmFacadeOrDependOnInfrastructure()
    {
        var removed = new[] { "IDbContext", "IDbContextFactory", "IDbContextTransaction", "IRepository`1",
            "IAsyncQueryable`1", "IAsyncQueryExecutor", "UpdateSetters`1", "IUpdateSetters`1",
            "IPersistenceSession", "IEntitySet`1", "IProviderQuery`1" };
        foreach (var assembly in new[] { typeof(IUnitOfWork).Assembly, typeof(IValueObject<>).Assembly })
        {
            Assert.DoesNotContain(assembly.GetExportedTypes(), type => removed.Contains(type.Name));
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name == "NOF.Infrastructure");
        }
        var save = Assert.Single(typeof(IUnitOfWork).GetMethods());
        Assert.Equal(nameof(IUnitOfWork.SaveChangesAsync), save.Name);
        Assert.Equal(typeof(Task), save.ReturnType);
        Assert.Equal(typeof(CancellationToken), Assert.Single(save.GetParameters()).ParameterType);
    }
}
