using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Contract;
using NOF.Hosting;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace NOF.Infrastructure;

public static class ApplicationServiceInvoker
{
    public static async Task<ServiceExecution> InvokeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)] TService>(
        IServiceProvider callerServiceProvider,
        MethodInfo serviceMethodInfo,
        object request,
        Context context,
        CancellationToken cancellationToken)
        where TService : class, IApplicationService
    {
        ArgumentNullException.ThrowIfNull(callerServiceProvider);
        ArgumentNullException.ThrowIfNull(serviceMethodInfo);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var operationName = serviceMethodInfo.Name;

        var outboundPipeline = callerServiceProvider.GetRequiredService<IRequestOutboundPipelineExecutor>();
        var outboundContext = new RequestOutboundContext(context)
        {
            ServiceType = typeof(TService),
            MethodInfo = serviceMethodInfo
        };

        await outboundPipeline.ExecuteAsync(outboundContext, request, async (currentContext, currentRequest, ct) =>
        {
            await using var inboundScope = callerServiceProvider.CreateAsyncScope();
            inboundScope.ServiceProvider.ResolveDaemonServices();

            var invocationResolver = inboundScope.ServiceProvider.GetRequiredService<ApplicationServiceInvocationResolver>();
            var resolution = invocationResolver.Resolve<TService>(operationName);
            var inboundPipeline = inboundScope.ServiceProvider.GetRequiredService<RequestInboundPipelineExecutor>();
            var inboundContext = await inboundPipeline.ExecuteAsync(
                currentRequest,
                resolution.HandlerMapping.HandlerType,
                resolution.HandlerMapping.ReturnType,
                typeof(TService),
                operationName,
                currentContext.Headers,
                ct).ConfigureAwait(false);
            currentContext.Execution = inboundContext.Execution;
        }, cancellationToken).ConfigureAwait(false);

        return outboundContext.Execution;
    }
}
