using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using NOF.Application.SourceGenerator;
using NOF.Contract;
using Xunit;

namespace NOF.SourceGenerator.Tests;

public class HandlerRegistrationGeneratorTests
{
    [Theory]
    [InlineData("base")]
    [InlineData("interface")]
    [InlineData("inherited")]
    [InlineData("record")]
    [InlineData("partial")]
    public void ApplicationHandlers_GenerateCompilableScopedRegistration(string kind)
    {
        const string method = "public System.Threading.Tasks.Task<int> HandleAsync(MyRequest request, Context context, System.Threading.CancellationToken cancellationToken) => System.Threading.Tasks.Task.FromResult(42);";
        var declaration = kind switch
        {
            "base" => "public sealed class Concrete : RequestHandler<MyRequest, int> { " + method.Replace("public ", "public override ") + " }",
            "interface" => "public sealed class Concrete : IRequestHandler<MyRequest, int> { " + method + " }",
            "record" => "public sealed record Concrete : IRequestHandler<MyRequest, int> { " + method + " }",
            "partial" => "public sealed partial class Concrete : IRequestHandler<MyRequest, int> { " + method + " } public sealed partial class Concrete { }",
            _ => "public abstract class Base<T> : RequestHandler<MyRequest, T> { public override System.Threading.Tasks.Task<T> HandleAsync(MyRequest request, Context context, System.Threading.CancellationToken cancellationToken) => throw new System.NotImplementedException(); } public sealed class Concrete : Base<int> { }"
        };
        var source = "using NOF.Application; using NOF.Contract; namespace App { public record MyRequest; " + declaration + " }";
        var (result, output) = RunApplicationGenerator(source);
        Assert.Empty(result.Diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var code = result.GeneratedTrees.Single().GetRoot().ToFullString();
        Assert.Contains("services.TryAddScoped<global::App.Concrete>();", code);
        Assert.DoesNotContain("ServiceDescriptor.Transient", code);
        Assert.DoesNotContain("GetTypes", code);
        Assert.DoesNotContain("Activator", code);
    }

    [Fact]
    public void ApplicationHandlers_PreserveNullableResponses_AndMultipleContracts()
    {
        const string source = """
            #nullable enable
            using NOF.Application;
            using NOF.Contract;
            namespace App;
            public record Query;
            public record OtherQuery;
            public sealed class Handler : IRequestHandler<Query, string?>, IRequestHandler<OtherQuery, int>
            {
                public System.Threading.Tasks.Task<string?> HandleAsync(Query request, Context context, System.Threading.CancellationToken cancellationToken)
                    => System.Threading.Tasks.Task.FromResult<string?>(null);
                public System.Threading.Tasks.Task<int> HandleAsync(OtherQuery request, Context context, System.Threading.CancellationToken cancellationToken)
                    => System.Threading.Tasks.Task.FromResult(42);
            }
            """;
        var (result, output) = RunApplicationGenerator(source);
        Assert.Empty(result.Diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(static diagnostic => diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error));
        var code = result.GeneratedTrees.Single().GetRoot().ToFullString();
        Assert.Contains("services.TryAddScoped<global::App.Handler>();", code);
        Assert.Equal(1, code.Split("services.TryAddScoped<global::App.Handler>();").Length - 1);
    }

    [Fact]
    public void ApplicationHandlers_AreScoped_Idempotent_AndIsolatedPerServiceCollection()
    {
        const string source = """
            using NOF.Application;
            using NOF.Contract;
            namespace GeneratedRuntime;
            public record Query;
            public sealed class Handler : IRequestHandler<Query, int>
            {
                public System.Threading.Tasks.Task<int> HandleAsync(Query request, Context context, System.Threading.CancellationToken cancellationToken)
                    => System.Threading.Tasks.Task.FromResult(42);
            }
            """;
        var (_, output) = RunApplicationGenerator(source);
        using var stream = new MemoryStream();
        var emission = output.Emit(stream);
        Assert.True(emission.Success, string.Join(Environment.NewLine, emission.Diagnostics));
        var assembly = System.Reflection.Assembly.Load(stream.ToArray());
        var attribute = Assert.Single(assembly.GetCustomAttributes(false).OfType<AssemblyInitializeAttribute>());
        var services = new ServiceCollection();
        attribute.Initialize(services);
        attribute.Initialize(services);
        var handler = Assert.Single(services, descriptor => descriptor.ServiceType.Assembly == assembly);
        Assert.Equal(ServiceLifetime.Scoped, handler.Lifetime);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        Assert.Same(first.ServiceProvider.GetRequiredService(handler.ServiceType), first.ServiceProvider.GetRequiredService(handler.ServiceType));
        Assert.NotSame(first.ServiceProvider.GetRequiredService(handler.ServiceType), second.ServiceProvider.GetRequiredService(handler.ServiceType));
        var independent = new ServiceCollection();
        attribute.Initialize(independent);
        Assert.Single(independent, descriptor => descriptor.ServiceType == handler.ServiceType);
        IServiceCollection conflicting = new ServiceCollection();
        var custom = ServiceDescriptor.Scoped(handler.ServiceType, handler.ServiceType);
        conflicting.Add(custom);
        attribute.Initialize(conflicting);
        Assert.Same(custom, Assert.Single(conflicting, descriptor => descriptor.ServiceType == handler.ServiceType));
    }

    [Fact]
    public void ApplicationHandlers_SharingInputAndOutput_AreRegisteredByUseCaseIdentity()
    {
        const string source = """
            using NOF.Application;
            using NOF.Contract;
            namespace App;
            public record Query;
            public abstract class Base : RequestHandler<Query, int>
            {
                public override System.Threading.Tasks.Task<int> HandleAsync(Query request, Context context, System.Threading.CancellationToken cancellationToken)
                    => System.Threading.Tasks.Task.FromResult(42);
            }
            public sealed class First : Base;
            public sealed class Second : Base;
            """;
        var (result, output) = RunApplicationGenerator(source);
        Assert.Empty(result.Diagnostics);
        Assert.Empty(output.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
        var code = result.GeneratedTrees.Single().GetRoot().ToFullString();
        Assert.Contains("services.TryAddScoped<global::App.First>();", code);
        Assert.Contains("services.TryAddScoped<global::App.Second>();", code);
    }

    [Fact]
    public void ApplicationHandlers_IgnoreAbstractOpenGenericAndUnrelatedTypes()
    {
        const string source = """
            using NOF.Application;
            using NOF.Contract;
            namespace App;
            public abstract class Base<T> : IRequestHandler<T, int>
            {
                public System.Threading.Tasks.Task<int> HandleAsync(T request, Context context, System.Threading.CancellationToken cancellationToken)
                    => System.Threading.Tasks.Task.FromResult(42);
            }
            public class Open<T> : Base<T>;
            public class Unrelated;
            """;
        var (result, _) = RunApplicationGenerator(source);
        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.GeneratedTrees);
    }

    private static (GeneratorDriverRunResult Result, Compilation Output) RunApplicationGenerator(string source)
    {
        var compilation = CSharpCompilation.CreateCompilation("GeneratedRequests" + Guid.NewGuid().ToString("N"), source, true,
            typeof(IServiceCollection), typeof(ServiceCollection), typeof(InitializedTypes), typeof(Context), typeof(IRequestHandler<,>));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new HandlerRegistrationGenerator().AsSourceGenerator()],
            parseOptions: (CSharpParseOptions)compilation.SyntaxTrees.First().Options);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return (driver.GetRunResult(), output);
    }

    [Fact]
    public void GeneratedCode_CommandHandler_RegistersConcreteOnly()
    {
        const string source = """
            using NOF.Application;
            using NOF.Abstraction;
            using NOF.Contract;
            namespace App
            {
                public record MyCommand;
                public class MyCommandHandler : CommandHandler<MyCommand>
                {
                    public override System.Threading.Tasks.Task HandleAsync(MyCommand command, Context context, System.Threading.CancellationToken cancellationToken) => throw new System.NotImplementedException();
                }
            }
            """;

        var comp = CSharpCompilation.CreateCompilation("App", source, isDll: true,
            typeof(IServiceCollection),
            typeof(InitializedTypes),
            typeof(Context),
            typeof(CommandHandler<>),
            typeof(CommandHandlerRegistration)
        );

        var result = new HandlerRegistrationGenerator().GetResult(comp);
        var generatedCode = result.GeneratedTrees.Single().GetRoot().ToFullString();

        Assert.Contains("[assembly: global::NOF.Abstraction.AssemblyInitializeAttribute<global::App.__AppHandlerAssemblyInitializer>]", generatedCode);
        Assert.Contains("services.InitializedTypes.Add(typeof(__AppHandlerAssemblyInitializer))", generatedCode);
        Assert.Contains("services.Add(global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton(typeof(__App_MyCommandHandlerCommandInboundInvoker), typeof(__App_MyCommandHandlerCommandInboundInvoker)));", generatedCode);
        Assert.Contains("services.GetOrAddSingleton<global::NOF.Application.CommandHandlerRegistry>().Add(new global::NOF.Application.CommandHandlerRegistration(typeof(global::App.MyCommandHandler), typeof(global::App.MyCommand), typeof(__App_MyCommandHandlerCommandInboundInvoker)));", generatedCode);
        Assert.Contains("public sealed class __App_MyCommandHandlerCommandInboundInvoker : global::NOF.Application.ICommandInboundHandlerInvoker", generatedCode);
        Assert.Contains("public string MessageTypeName => \"App.MyCommand\";", generatedCode);
        Assert.Contains("return deserialize(payload, typeof(global::App.MyCommand))", generatedCode);
        Assert.Contains("GetRequiredService<global::App.MyCommandHandler>(services);", generatedCode);
        Assert.Contains("handler.HandleAsync((global::App.MyCommand)message, context, cancellationToken)", generatedCode);
        Assert.DoesNotContain("IMessagePayloadBinder", generatedCode);
        Assert.DoesNotContain("IMessageTypeBinder", generatedCode);
        Assert.DoesNotContain("TypeResolver.Register", generatedCode);
        Assert.DoesNotContain("SourceModule.ReferencedAssemblySymbols", generatedCode);

    }

    [Fact]
    public void GeneratedCode_NotificationHandler_RegistersBothConcreteAndInterfaceFactory()
    {
        const string source = """
            using NOF.Application;
            using NOF.Abstraction;
            using NOF.Contract;
            namespace App
            {
                public record MyNotification;
                public class MyNotificationHandler : NotificationHandler<MyNotification>
                {
                    public override System.Threading.Tasks.Task HandleAsync(MyNotification notification, Context context, System.Threading.CancellationToken cancellationToken) => throw new System.NotImplementedException();
                }
            }
            """;

        var comp = CSharpCompilation.CreateCompilation("App", source, isDll: true,
            typeof(IServiceCollection),
            typeof(InitializedTypes),
            typeof(Context),
            typeof(NotificationHandler<>),
            typeof(NotificationHandlerRegistration)
        );

        var result = new HandlerRegistrationGenerator().GetResult(comp);
        var generatedCode = result.GeneratedTrees.Single().GetRoot().ToFullString();

        Assert.Contains("[assembly: global::NOF.Abstraction.AssemblyInitializeAttribute<global::App.__AppHandlerAssemblyInitializer>]", generatedCode);
        Assert.Contains("services.InitializedTypes.Add(typeof(__AppHandlerAssemblyInitializer))", generatedCode);
        Assert.Contains("services.Add(global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton(typeof(__App_MyNotificationHandlerNotificationInboundInvoker), typeof(__App_MyNotificationHandlerNotificationInboundInvoker)));", generatedCode);
        Assert.Contains("services.GetOrAddSingleton<global::NOF.Application.NotificationHandlerRegistry>().Add(new global::NOF.Application.NotificationHandlerRegistration(typeof(global::App.MyNotificationHandler), typeof(global::App.MyNotification), typeof(__App_MyNotificationHandlerNotificationInboundInvoker)));", generatedCode);
        Assert.Contains("public sealed class __App_MyNotificationHandlerNotificationInboundInvoker : global::NOF.Application.INotificationInboundHandlerInvoker", generatedCode);
        Assert.Contains("public string MessageTypeName => \"App.MyNotification\";", generatedCode);
        Assert.Contains("return deserialize(payload, typeof(global::App.MyNotification))", generatedCode);
        Assert.Contains("GetRequiredService<global::App.MyNotificationHandler>(services);", generatedCode);
        Assert.Contains("handler.HandleAsync((global::App.MyNotification)message, context, cancellationToken)", generatedCode);
        Assert.DoesNotContain("IMessagePayloadBinder", generatedCode);
        Assert.DoesNotContain("IMessageTypeBinder", generatedCode);
        Assert.DoesNotContain("TypeResolver.Register", generatedCode);
        Assert.DoesNotContain("SourceModule.ReferencedAssemblySymbols", generatedCode);
    }
}
