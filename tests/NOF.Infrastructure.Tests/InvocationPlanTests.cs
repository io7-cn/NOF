using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using Xunit;

namespace NOF.Infrastructure.Tests;

public sealed class InvocationPlanTests
{
    private static ServiceOperationDescriptor<Query, string> Read => new(typeof(IService), nameof(IService.Read));
    private static ServiceOperationDescriptor<Query, string> Other => new(typeof(IService), nameof(IService.Other));

    [Fact]
    public async Task Scopes_NestInOrder_WithStableTiesAndOnlyOneInstancePerType()
    {
        var services = new ServiceCollection().UseInMemoryInvocationPipeline();
        services.AddSingleton<List<string>>();
        services.AddInvocationFilters(filters =>
        {
            filters.Global.Use<First>(10).Use<Second>(-10);
            filters.Service<IService>().Use<First>().Use<Third>(-100);
            filters.UseCase(Read).Use<Fourth>(-1000).Use<Fifth>(-1000);
        });
        using var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<IInvocationPipeline>();
        var plan = pipeline.Describe(CreateContext(Read));
        Assert.Equal([typeof(Second), typeof(First), typeof(Third), typeof(Fourth), typeof(Fifth)], plan.Filters.Select(item => item.FilterType));
        var result = await pipeline.ExecuteAsync(CreateContext(Read), new Query(), (_, _, _) => ValueTask.FromResult<object?>("ok"), default);
        Assert.Equal("ok", result);
        Assert.Equal(["Second+", "First+", "Third+", "Fourth+", "Fifth+", "Fifth-", "Fourth-", "Third-", "First-", "Second-"], provider.GetRequiredService<List<string>>());
    }

    [Theory]
    [InlineData(InvocationOrigin.Local)]
    [InlineData(InvocationOrigin.Http)]
    [InlineData(InvocationOrigin.Message)]
    public async Task Exclude_RemovesRuntimeAndDescription_ReenableUsesInnerPosition(InvocationOrigin origin)
    {
        var services = new ServiceCollection().UseInMemoryInvocationPipeline();
        services.AddSingleton<List<string>>();
        services.AddScoped<CatalogFilter>(_ => throw new InvalidOperationException("Excluded filter constructed"));
        services.AddInvocationFilters(filters =>
        {
            filters.Global.Use<CatalogFilter>(5).Use<First>();
            filters.Service<IService>().Exclude<CatalogFilter>();
            filters.UseCase(Read).Use<CatalogFilter>(-100);
        });
        using var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<IInvocationPipeline>();
        var excluded = pipeline.Describe(CreateContext(Other, origin));
        Assert.Empty(excluded.FailureCases);
        Assert.DoesNotContain(excluded.Filters, item => item.FilterType == typeof(CatalogFilter));
        Assert.Equal("ok", await pipeline.ExecuteAsync(CreateContext(Other, origin), new Query(), (_, _, _) => ValueTask.FromResult<object?>("ok"), default));
        var enabled = pipeline.Describe(CreateContext(Read, origin));
        Assert.Equal([typeof(First), typeof(CatalogFilter)], enabled.Filters.Select(item => item.FilterType));
        Assert.Equal(InvocationFilterScope.UseCase, enabled.Filters[1].Scope);
        Assert.Equal(-100, enabled.Filters[1].Order);
        Assert.Equal([typeof(CatalogFilter.Denied), typeof(CatalogFilter.Conflict)], enabled.FailureCases.Select(item => item.CaseType));
    }

    [Fact]
    public void ExcludedFilter_DescribeIsNeverCalled_AndRulesAreHostLocalAndFrozen()
    {
        var one = new ServiceCollection().UseInMemoryInvocationPipeline();
        one.AddInvocationFilters(filters =>
        {
            filters.Global.Use<InvalidDescription>();
            filters.Service<IService>().Exclude<InvalidDescription>();
        });
        using var first = one.BuildServiceProvider();
        using var second = new ServiceCollection().UseInMemoryInvocationPipeline().BuildServiceProvider();
        Assert.Empty(first.GetRequiredService<IInvocationPipeline>().Describe(CreateContext(Read)).FailureCases);
        Assert.Empty(second.GetRequiredService<IInvocationPipeline>().Describe(CreateContext(Read)).Filters);
        Assert.Throws<InvalidOperationException>(() => one.AddInvocationFilter<First>());
    }

    [Fact]
    public void Rules_DoNotLeakToSiblingServicesOrOperations()
    {
        var services = new ServiceCollection().UseInMemoryInvocationPipeline();
        services.AddInvocationFilters(filters =>
        {
            filters.Service<IService>().Use<First>();
            filters.UseCase(Read).Use<Second>();
        });
        using var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<IInvocationPipeline>();
        Assert.Equal([typeof(First)], pipeline.Describe(CreateContext(Other)).Filters.Select(item => item.FilterType));
        var sibling = new ServiceOperationDescriptor<Query, string>(typeof(ISibling), nameof(ISibling.Read));
        Assert.Empty(pipeline.Describe(CreateContext(sibling)).Filters);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SameScope_UseAndExcludeUseTheLastDeclaration(bool excludeFirst)
    {
        var services = new ServiceCollection().UseInMemoryInvocationPipeline();
        services.AddInvocationFilters(filters =>
        {
            if (excludeFirst)
            {
                filters.Global.Exclude<First>();
                filters.Global.Use<First>();
            }
            else
            {
                filters.Global.Use<First>();
                filters.Global.Exclude<First>();
            }
        });
        using var provider = services.BuildServiceProvider();
        var filters = provider.GetRequiredService<IInvocationPipeline>().Describe(CreateContext(Read)).Filters;
        Assert.Equal(excludeFirst ? 1 : 0, filters.Count);
    }

    [Fact]
    public void MoreSpecificOrder_OverridesWithoutConstructingFilters()
    {
        var services = new ServiceCollection().UseInMemoryInvocationPipeline();
        services.AddInvocationFilters(filters =>
        {
            filters.Global.Use<CatalogFilter>(10);
            filters.Service<IService>().Use<CatalogFilter>(20);
        });
        using var provider = services.BuildServiceProvider();
        var plan = provider.GetRequiredService<IInvocationPipeline>().Describe(CreateContext(Read));
        Assert.Equal(20, Assert.Single(plan.Filters).Order);
        using var sameScope = new ServiceCollection().UseInMemoryInvocationPipeline()
            .AddInvocationFilters(filters => filters.Global.Use<First>(1).Use<First>(2)).BuildServiceProvider();
        Assert.Equal(2, Assert.Single(sameScope.GetRequiredService<IInvocationPipeline>().Describe(CreateContext(Read)).Filters).Order);
    }

    [Fact]
    public async Task Describe_SelectsOnlyOwnDeclaredCases_AndRuntimeRejectsUndeclaredValues()
    {
        var services = new ServiceCollection().UseInMemoryInvocationPipeline().AddInvocationFilter<SubsetFilter>();
        using var provider = services.BuildServiceProvider();
        var pipeline = provider.GetRequiredService<IInvocationPipeline>();
        var plan = pipeline.Describe(CreateContext(Read));
        Assert.Equal(typeof(CatalogFilter.Denied), Assert.Single(plan.FailureCases).CaseType);
        Assert.Throws<InvalidOperationException>(() => plan.Validate(new CatalogFilter.Conflict(new ConflictDetails(42))));
        var result = await pipeline.ExecuteAsync(CreateContext(Read), new Query(), (_, _, _) => throw new InvalidOperationException("Should short circuit"), default);
        Assert.IsType<CatalogFilter.Denied>(result);
        using var invalid = new ServiceCollection().UseInMemoryInvocationPipeline().AddInvocationFilter<InvalidDescription>().BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => invalid.GetRequiredService<IInvocationPipeline>().Describe(CreateContext(Read)));
    }

    [Fact]
    public async Task Filter_ReturnCanTransformBusinessValue_WithoutMutableContextState()
    {
        using var provider = new ServiceCollection().UseInMemoryInvocationPipeline().AddInvocationFilter<Transform>().BuildServiceProvider();
        var result = await provider.GetRequiredService<IInvocationPipeline>().ExecuteAsync(CreateContext(Read), new Query(), (_, _, _) => ValueTask.FromResult<object?>("business"), default);
        Assert.Equal("business filtered", result);
        Assert.Throws<InvalidOperationException>(() => new InvocationPlan(Read, InvocationOrigin.Local, [], []).Validate(42));
    }

    [Fact]
    public void Describe_UsesCapturedImplementationMetadata_WithoutConstructingHandlerOrFilter()
    {
        var services = new ServiceCollection().UseInMemoryInvocationPipeline().AddInvocationFilter<MetadataFilter>();
        var descriptor = Read;
        var registry = services.GetOrAddSingleton<ApplicationServiceRegistry>();
        registry.AddOperationImplementation(typeof(ServiceOperation<Query, string>), typeof(Handler));
        registry.Add(new(typeof(IService), typeof(object))
        {
            HandlerMappings = new Dictionary<string, ServiceOperationMapping>
            {
                [nameof(IService.Read)] = new(typeof(ServiceOperation<Query, string>), typeof(Query), typeof(string)) { Descriptor = descriptor }
            }
        });
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<InvocationPlanFactory>().ValidateConfiguration();
        var plan = provider.GetRequiredService<IInvocationPlanProvider>().Describe(descriptor, InvocationOrigin.Http);
        Assert.Equal(typeof(CatalogFilter.Denied), Assert.Single(plan.FailureCases).CaseType);
    }

    [Fact]
    public void GeneratedCatalog_RetainsOpaqueCodesMessagesAndCustomPayload_WithoutResultWrapper()
    {
        var denied = new CatalogFilter.Denied("denied");
        var entry = CatalogFilter.FailureCases.Single(item => item.CaseType == typeof(CatalogFilter.Denied));
        Assert.Equal("000403", entry.Title);
        Assert.Equal("000403", denied.Title);
        Assert.Equal("denied", entry.ReadDetail(denied));
        var details = new ConflictDetails(42);
        var custom = CatalogFilter.FailureCases.Single(item => item.CaseType == typeof(CatalogFilter.Conflict));
        Assert.Equal(typeof(ConflictDetails), custom.ErrorType);
        Assert.Equal("Conflict", custom.Title);
        Assert.Same(details, custom.ReadError(new CatalogFilter.Conflict(details)));
        Assert.Null(typeof(CatalogFilter).GetNestedType("Result"));
        Assert.DoesNotContain(typeof(CatalogFilter).GetMethods(), method => method.Name.StartsWith("Create", StringComparison.Ordinal));
    }

    private static InvocationContext CreateContext(ServiceOperationDescriptor operation, InvocationOrigin origin = InvocationOrigin.Local) => new()
    {
        Operation = operation,
        Origin = origin,
        HandlerType = typeof(Handler),
        HandlerMethodInfo = typeof(Handler).GetMethod(nameof(Handler.HandleAsync), [typeof(Query), typeof(InvocationContext), typeof(CancellationToken)])!
    };

    public sealed record Query;
    public interface IService : IApplicationService { string Read(Query input); string Other(Query input); }
    public interface ISibling : IApplicationService { string Read(Query input); }
    public sealed class Handler : ServiceOperation<Query, string>
    {
        public Handler() => throw new InvalidOperationException("Metadata must not construct handlers");
        [RequirePermission("read")]
        public override Task<string> HandleAsync(Query request, InvocationContext context, CancellationToken cancellationToken) => Task.FromResult("ok");
    }
    public abstract class Recording(List<string> log) : IInvocationFilter
    {
        public async ValueTask<object?> InvokeAsync(InvocationContext context, object input, InvocationDelegate next, CancellationToken ct)
        {
            log.Add(GetType().Name + "+");
            var result = await next(context, input, ct);
            log.Add(GetType().Name + "-");
            return result;
        }
    }
    public sealed class First(List<string> log) : Recording(log);
    public sealed class Second(List<string> log) : Recording(log);
    public sealed class Third(List<string> log) : Recording(log);
    public sealed class Fourth(List<string> log) : Recording(log);
    public sealed class Fifth(List<string> log) : Recording(log);
    public sealed class SubsetFilter : IInvocationFilter
    {
        public static IReadOnlyList<InvocationFailureCase> FailureCases => CatalogFilter.FailureCases;
        public static void Describe(InvocationDescriptionContext description) => description.Include<CatalogFilter.Denied>();
        public ValueTask<object?> InvokeAsync(InvocationContext context, object input, InvocationDelegate next, CancellationToken ct) => ValueTask.FromResult<object?>(new CatalogFilter.Denied());
    }
    public sealed class InvalidDescription : IInvocationFilter
    {
        public static void Describe(InvocationDescriptionContext description) => description.Include<CatalogFilter.Denied>();
        public ValueTask<object?> InvokeAsync(InvocationContext context, object input, InvocationDelegate next, CancellationToken ct) => next(context, input, ct);
    }
    public sealed class MetadataFilter : IInvocationFilter
    {
        public MetadataFilter() => throw new InvalidOperationException("Metadata must not construct filters");
        public static IReadOnlyList<InvocationFailureCase> FailureCases => CatalogFilter.FailureCases;
        public static void Describe(InvocationDescriptionContext description)
        {
            if (description.GetMetadata<RequirePermissionAttribute>()?.Permission == "read")
            {
                description.Include<CatalogFilter.Denied>();
            }
        }
        public ValueTask<object?> InvokeAsync(InvocationContext context, object input, InvocationDelegate next, CancellationToken ct) => next(context, input, ct);
    }
    public sealed class Transform : IInvocationFilter
    {
        public async ValueTask<object?> InvokeAsync(InvocationContext context, object input, InvocationDelegate next, CancellationToken ct) => (string)(await next(context, input, ct))! + " filtered";
    }
}

public sealed record ConflictDetails(int Id);

[FailureCase("Denied", "000403")]
[FailureCase("Conflict", "Conflict", typeof(ConflictDetails))]
public sealed partial class CatalogFilter : IInvocationFilter
{
    public CatalogFilter() => throw new InvalidOperationException("Catalog metadata must not construct a filter");
    public ValueTask<object?> InvokeAsync(InvocationContext context, object input, InvocationDelegate next, CancellationToken ct) => next(context, input, ct);
}
