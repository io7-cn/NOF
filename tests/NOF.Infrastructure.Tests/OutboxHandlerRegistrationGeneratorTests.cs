using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NOF.Abstraction;
using NOF.Application;
using NOF.Hosting;
using NOF.Infrastructure.SourceGenerator;
using System.Reflection;
using Xunit;

namespace NOF.Infrastructure.Tests;

public sealed class OutboxHandlerRegistrationGeneratorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplicationPart_RegistersSchemasOnceWithoutConstruction_AndActivationRemainsExplicit(bool enableFirst)
    {
        var (output, _) = Generate("""
            public sealed record Work(string Value);
            public abstract class Adapter<T> : OutboxHandler<T>;
            public sealed class Handler : Adapter<Work>
            {
                public Handler() => throw new InvalidOperationException("Do not construct while registering or staging.");
                public override ValueTask HandleAsync(Work message, OutboxDeliveryContext context, CancellationToken ct)
                    => throw new InvalidOperationException();
            }
            public sealed class TextHandler : OutboxHandler<string>
            {
                public override ValueTask HandleAsync(string message, OutboxDeliveryContext context, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }
            public static class Producer
            {
                public static ValueTask<Guid> Stage(IOutbox outbox)
                    => outbox.EnqueueAsync(new Work("value"), Context.Empty);
            }
            """);
        using var stream = new MemoryStream();
        var result = output.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        var assembly = Assembly.Load(stream.ToArray());
        var services = new ServiceCollection();
        var store = new Moq.Mock<IOutboxStore>();
        services.AddSingleton(store.Object);
        if (enableFirst)
        {
            services.AddOutbox();
        }
        services.AddApplicationPart(assembly);
        services.AddApplicationPart(assembly);
        Assert.Single(services, item => item.ServiceType == assembly.GetType("Handler"));
        if (!enableFirst)
        {
            Assert.DoesNotContain(services, item => item.ServiceType == typeof(IOutbox) || item.ServiceType == typeof(IHostedService));
            services.AddOutbox();
        }
        services.AddOutbox();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();
        var registry = provider.GetRequiredService<OutboxRegistry>();
        var schema = registry.Get(assembly.GetType("Work")!);
        Assert.Equal(assembly.GetType("Work")!.ToString(), schema.MessageType);
        var stage = assembly.GetType("Producer")!.GetMethod("Stage")!.CreateDelegate<Func<IOutbox, ValueTask<Guid>>>();
        await stage(scope.ServiceProvider.GetRequiredService<IOutbox>());
        store.Verify(value => value.Stage(Moq.It.Is<NOFOutboxMessage>(record => record.MessageType == "Work")), Moq.Times.Once);
        await scope.ServiceProvider.GetRequiredService<IOutbox>().EnqueueAsync("value", Context.Empty);
        store.Verify(value => value.Stage(Moq.It.Is<NOFOutboxMessage>(record => record.MessageType == typeof(string).ToString())), Moq.Times.Once);
        using var second = new ServiceCollection().AddOutbox().BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => second.GetRequiredService<OutboxRegistry>().Get(typeof(string)));
    }

    [Theory]
    [InlineData("public const string MessageType = \"legacy\";")]
    [InlineData("public const int SchemaVersion = 0;")]
    [InlineData("public const int SchemaVersion = 1;")]
    [InlineData("public const string SchemaVersion = \"1\";")]
    [InlineData("private const int SchemaVersion = 1;")]
    [InlineData("public enum Version { One = 1 } public const Version SchemaVersion = Version.One;")]
    public void InvalidMetadata_ReportsNOF330(string members)
    {
        var (_, diagnostics) = Generate($$"""
            public sealed class Handler : OutboxHandler<string>
            {
                {{members}}
                public override ValueTask HandleAsync(string message, OutboxDeliveryContext context, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }
            """, expectErrors: true);
        Assert.Contains(diagnostics, item => item.Id == "NOF330" && item.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void DuplicateMessageType_ReportsNOF331()
    {
        var (_, diagnostics) = Generate($$"""
            public sealed class One : OutboxHandler<string>
            {
                public override ValueTask HandleAsync(string message, OutboxDeliveryContext context, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }
            public sealed class Two : OutboxHandler<string>
            {
                public override ValueTask HandleAsync(string message, OutboxDeliveryContext context, CancellationToken ct)
                    => ValueTask.CompletedTask;
            }
            """, expectErrors: true);
        Assert.Contains(diagnostics, item => item.Id == "NOF331" && item.Severity == DiagnosticSeverity.Error);
    }

    private static (Compilation Output, IReadOnlyList<Diagnostic> Diagnostics) Generate(string declaration, bool expectErrors = false)
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using NOF.Infrastructure;
            using NOF.Application;
            using NOF.Abstraction;
            """ + "\n" + declaration;
        var input = CSharpCompilation.CreateCompilation("Outbox_" + Guid.NewGuid().ToString("N"), source, true,
            typeof(OutboxHandler<>), typeof(IAssemblyInitializer), typeof(IServiceCollection));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new OutboxHandlerRegistrationGenerator().AsSourceGenerator()],
            parseOptions: new CSharpParseOptions(LanguageVersion.Preview));
        driver.RunGeneratorsAndUpdateCompilation(input, out var output, out var diagnostics);
        if (!expectErrors)
        {
            Assert.DoesNotContain(diagnostics.Concat(output.GetDiagnostics()), item => item.Severity == DiagnosticSeverity.Error);
        }
        return (output, diagnostics);
    }
}
