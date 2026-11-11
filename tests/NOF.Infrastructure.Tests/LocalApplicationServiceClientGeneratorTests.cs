using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NOF.Application;
using NOF.Contract;
using NOF.Infrastructure;
using NOF.Infrastructure.SourceGenerator;
using Xunit;

namespace NOF.SourceGenerator.Tests;

public class LocalApplicationServiceClientGeneratorTests
{
    private static readonly Type[] _extraRefs =
    [
        typeof(ApplicationService<>),
        typeof(ApplicationServiceInvoker),
        typeof(IApplicationServiceClient),
        typeof(IApplicationServiceClient<>),
        typeof(IApplicationService),
        typeof(Result),
        typeof(Result<>),
        typeof(IAsyncEnumerable<>),
        typeof(TransportOverMemoryAttribute)
    ];

    [Fact]
    public void ResultT_ReturnType_UsesDirectResult()
    {
        const string source = """
                              using NOF.Contract;
                              using NOF.Application;
                              using NOF.Infrastructure;

                              namespace MyApp
                              {
                                  public sealed record Empty;
                                  public sealed record GetFleetOverviewResponse(string Name);

                                  public partial interface IDroneOpsService : IApplicationService
                                  {
                                      Result<GetFleetOverviewResponse> GetFleetOverview(Empty request);
                                  }

                                  public partial interface IDroneOpsServiceClient : IApplicationServiceClient<IDroneOpsService>
                                  {
                                      global::System.Threading.Tasks.Task<Result<GetFleetOverviewResponse>> GetFleetOverviewAsync(
                                          Empty request,
                                          Context context,
                                          global::System.Threading.CancellationToken cancellationToken = default);
                                  }

                                  public abstract class DroneOpsServer : ApplicationService<IDroneOpsService>;
                              }
                              """;

        var runResult = new LocalApplicationServiceClientGenerator().GetResultPostGen(source, _extraRefs);
        var code = GetGeneratedCode(runResult);

        Assert.Contains(
            "result.Require<global::NOF.Contract.Result<global::MyApp.GetFleetOverviewResponse>>(static failure => global::NOF.Contract.Result<global::MyApp.GetFleetOverviewResponse>.CreateFailure(failure), allowNull: false)",
            code);
        Assert.Contains("public sealed class LocalDroneOpsServerClient : global::MyApp.IDroneOpsServiceClient", code);
        Assert.Contains("private static readonly global::System.Reflection.MethodInfo __GetFleetOverviewMethodInfo_0 =", code);
        Assert.Contains("ApplicationServiceInvoker.InvokeAsync<global::MyApp.IDroneOpsService>(_serviceProvider, __GetFleetOverviewMethodInfo_0, request, context, cancellationToken)", code);
        Assert.DoesNotContain(
            "((global::NOF.Contract.IResult)result!)",
            code);
        Assert.DoesNotContain(
            "(global::MyApp.Result<global::MyApp.GetFleetOverviewResponse>)result!",
            code);
        Assert.DoesNotContain(
            "(global::NOF.Contract.Result<global::MyApp.GetFleetOverviewResponse>)result!",
            code);
    }

    [Fact]
    public void StreamingResult_ReturnType_UsesDirectResult()
    {
        const string source = """
                              using NOF.Contract;
                              using NOF.Application;
                              using NOF.Infrastructure;
                              using System.Collections.Generic;

                              namespace MyApp
                              {
                                  public sealed record Empty;
                                  public sealed record DroneEvent(string Name);

                                  public partial interface IDroneOpsService : IApplicationService
                                  {
                                      Result<global::System.Collections.Generic.IAsyncEnumerable<DroneEvent>> StreamEvents(Empty request);
                                  }

                                  public partial interface IDroneOpsServiceClient : IApplicationServiceClient<IDroneOpsService>
                                  {
                                      global::System.Threading.Tasks.Task<Result<global::System.Collections.Generic.IAsyncEnumerable<DroneEvent>>> StreamEventsAsync(
                                          Empty request,
                                          Context context,
                                          global::System.Threading.CancellationToken cancellationToken = default);
                                  }

                                  public abstract class DroneOpsServer : ApplicationService<IDroneOpsService>;
                              }
                              """;

        var runResult = new LocalApplicationServiceClientGenerator().GetResultPostGen(source, _extraRefs);
        var code = GetGeneratedCode(runResult);

        Assert.Contains("Task<global::NOF.Contract.Result<global::System.Collections.Generic.IAsyncEnumerable<global::MyApp.DroneEvent>>> StreamEventsAsync", code);
        Assert.Contains(
            "result.Require<global::NOF.Contract.Result<global::System.Collections.Generic.IAsyncEnumerable<global::MyApp.DroneEvent>>>(static failure => global::NOF.Contract.Result<global::System.Collections.Generic.IAsyncEnumerable<global::MyApp.DroneEvent>>.CreateFailure(failure), allowNull: false)",
            code);
    }

    [Fact]
    public void CustomResult_ReturnType_UsesDirectResult()
    {
        const string source = """
                              using NOF.Abstraction;
                              using NOF.Contract;
                              using NOF.Application;
                              using NOF.Infrastructure;
                              using System.Collections.Generic;

                              namespace MyApp
                              {
                                  public sealed record Empty;
                                  public union FleetOverviewResult(Success<string>, Failure) : IRpcResponse<FleetOverviewResult>
                                  {
                                      public static FleetOverviewResult CreateFailure(Failure failure) => failure;
                                      public static FleetOverviewResult CreateEmptyResponse() => throw new System.InvalidOperationException();
                                  }

                                  public partial interface IDroneOpsService : IApplicationService
                                  {
                                      FleetOverviewResult GetFleetOverview(Empty request);
                                  }

                                  public partial interface IDroneOpsServiceClient : IApplicationServiceClient<IDroneOpsService>
                                  {
                                      global::System.Threading.Tasks.Task<FleetOverviewResult> GetFleetOverviewAsync(
                                          Empty request,
                                          Context context,
                                          global::System.Threading.CancellationToken cancellationToken = default);
                                  }

                                  public abstract class DroneOpsServer : ApplicationService<IDroneOpsService>;
                              }
                              """;

        var runResult = new LocalApplicationServiceClientGenerator().GetResultPostGen(source, _extraRefs);
        var code = GetGeneratedCode(runResult);

        Assert.Contains("Task<global::MyApp.FleetOverviewResult> GetFleetOverviewAsync", code);
        Assert.Contains(
            "result.Require<global::MyApp.FleetOverviewResult>(static failure => global::MyApp.FleetOverviewResult.CreateFailure(failure), allowNull: false)",
            code);
    }

    [Fact]
    public void GeneratesLocalClient_ForGenericRpcClient()
    {
        const string source = """
                              using NOF.Contract;
                              using NOF.Application;
                              using NOF.Infrastructure;

                              namespace MyApp
                              {
                                  public sealed class Payload
                                  {
                                  }

                                  public sealed record Query<TValue>(TValue Value);

                                  public partial interface IMyService<TValue> : IApplicationService
                                      where TValue : class, new()
                                  {
                                      Result<TValue> Get(Query<TValue> request);
                                  }

                                  public partial interface IMyServiceClient<TValue> : IApplicationServiceClient<IMyService<TValue>>
                                      where TValue : class, new()
                                  {
                                      global::System.Threading.Tasks.Task<Result<TValue>> GetAsync(
                                          Query<TValue> request,
                                          Context context,
                                          global::System.Threading.CancellationToken cancellationToken = default);
                                  }

                                  public abstract class MyServiceServer<TValue> : ApplicationService<IMyService<TValue>>
                                      where TValue : class, new();
                              }
                              """;

        var runResult = new LocalApplicationServiceClientGenerator().GetResultPostGen(source, _extraRefs);
        var code = runResult.GeneratedTrees
            .Select(tree => tree.GetRoot().ToFullString())
            .Single(generated => generated.Contains("class LocalMyServiceServerClient<TValue>"));

        Assert.Contains("public sealed class LocalMyServiceServerClient<TValue> : global::MyApp.IMyServiceClient<TValue>", code);
        Assert.Contains("where TValue : class, new()", code);
        Assert.Contains("ApplicationServiceInvoker.InvokeAsync<global::MyApp.IMyService<TValue>>", code);
    }

    [Fact]
    public void TransportOverMemory_WithoutClientGeneratorOutput_StillProducesLocalClient()
    {
        const string source = """
                              using NOF.Application;
                              using NOF.Contract;

                                  namespace MyApp
                                  {
                                      public sealed record Empty;

                                      [TransportOverMemory]
                                      public interface IDroneOpsService : IApplicationService
                                  {
                                      Result Ping(Empty request);
                                  }

                                  public abstract class DroneOpsServer : ApplicationService<IDroneOpsService>;
                              }
                              """;

        var extraReferences = _extraRefs.Select(type => type.ToMetadataReference()).ToArray();
        var compilation = CSharpCompilation.CreateCompilation("TestAssembly", source, true, extraReferences);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new LocalApplicationServiceClientGenerator());

        driver = driver.RunGenerators(compilation);

        var code = GetGeneratedCode(driver.GetRunResult());
        Assert.Contains("public sealed class LocalDroneOpsServerClient : global::MyApp.IDroneOpsServiceClient", code);
        Assert.Contains("Task<global::NOF.Contract.Result> PingAsync", code);
    }

    private static string GetGeneratedCode(GeneratorDriverRunResult runResult)
        => runResult.GeneratedTrees
            .Select(tree => tree.GetRoot().ToFullString())
            .Single(code => code.Contains("class LocalDroneOpsServerClient"));

}
