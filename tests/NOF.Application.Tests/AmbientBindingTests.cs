using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Domain;
using Xunit;

namespace NOF.Application.Tests;

public sealed class AmbientBindingTests
{
    [Fact]
    public async Task Bindings_AreExplicit_NestAcrossAwaits_AndRestoreOnException()
    {
        var services = new ServiceCollection();
        services.AddMapping<int, string>(value => value.ToString());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        _ = scope.ServiceProvider.GetRequiredService<IMapper>();
        Assert.Throws<InvalidOperationException>(() => Mapper.Current);
        var original = Context.Current;
        using (scope.ServiceProvider.BindNOF(Context.Empty.WithTenantId("outer")))
        {
            var mapper = Mapper.Current;
            Assert.Same(scope.ServiceProvider.GetRequiredService<IMapper>(), mapper);
            await Task.Yield();
            Assert.Equal("outer", Context.Current.TenantId);
            Assert.Throws<InvalidOperationException>((Action)(() =>
            {
                using var inner = scope.ServiceProvider.BindNOF(Context.Empty.WithTenantId("inner"));
                Assert.Equal("inner", Context.Current.TenantId);
                throw new InvalidOperationException("expected");
            }));
            Assert.Equal("outer", Context.Current.TenantId);
            Assert.Same(mapper, Mapper.Current);
            using var empty = new ServiceCollection().BuildServiceProvider();
            using (empty.BindNOF(Context.Empty))
            {
                Assert.Throws<InvalidOperationException>(() => Mapper.Current);
                Assert.Throws<InvalidOperationException>(() => IdGenerator.Current);
            }

            Assert.Same(mapper, Mapper.Current);
        }

        Assert.Same(original, Context.Current);
        Assert.Throws<InvalidOperationException>(() => Mapper.Current);
    }

    [Fact]
    public async Task ParallelBindings_DoNotLeakTenantOrServices()
    {
        var tasks = Enumerable.Range(0, 8).Select(async index =>
        {
            var services = new ServiceCollection();
            services.AddMapping<int, string>(value => "mapped:" + value);
            await using var provider = services.BuildServiceProvider();
            using var binding = provider.BindNOF(Context.Empty.WithTenantId(index.ToString()));
            var mapper = Mapper.Current;
            await Task.Yield();
            Assert.Equal(index.ToString(), Context.Current.TenantId);
            Assert.Same(mapper, Mapper.Current);
            Assert.Equal("mapped:1", new[] { 1 }.AsQueryable().ProjectTo<string>().Single());
        });
        await Task.WhenAll(tasks);
        Assert.Throws<InvalidOperationException>(() => Mapper.Current);
    }

    [Fact]
    public void ConvenienceFactories_AreLazyAndUseTheActualInvocationContext()
    {
        string? observedTenant = null;
        var services = new ServiceCollection();
        services.AddScoped<IIdGenerator>(_ => { observedTenant = Context.Current.TenantId; return new TestGenerator(); });
        services.AddScoped<IMapper>(_ => throw new InvalidOperationException("factory failed"));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var original = Context.Current;
        using (scope.ServiceProvider.BindNOF(Context.Empty.WithTenantId("requested")))
        {
            Assert.Null(observedTenant);
            using (Context.PushCurrent(Context.Current.WithTenantId("normalized")))
            {
                Assert.Equal(42, IdGenerator.Current.NextId());
                Assert.Equal("normalized", observedTenant);
            }
            Assert.Throws<InvalidOperationException>(() => Mapper.Current);
            Assert.Equal("requested", Context.Current.TenantId);
        }
        Assert.Same(original, Context.Current);
        Assert.Throws<InvalidOperationException>(() => Mapper.Current);
    }

    public sealed class TestGenerator : IIdGenerator
    {
        public long NextId() => 42;
    }
}
