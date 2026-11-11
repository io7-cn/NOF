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
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.TryGetItem(NOFAbstractionConstants.Transport.Headers.TenantId, out _))
        {
            context = context.WithTenantId(Context.Current.TenantId);
        }
        var callerUser = callerServiceProvider.GetRequiredService<IUserContext>();
        var identities = callerUser.User.Identities.Select(static identity => identity.Clone()).ToArray();
        await using var inboundScope = callerServiceProvider.CreateAsyncScope();
        var inboundUser = inboundScope.ServiceProvider.GetRequiredService<IUserContext>();
        if (!ReferenceEquals(callerUser, inboundUser))
        {
            inboundUser.Logout();
            inboundUser.User.AddIdentities(identities);
        }
        inboundScope.ServiceProvider.ResolveDaemonServices();
        var operationName = serviceMethodInfo.Name;
        var resolution = inboundScope.ServiceProvider.GetRequiredService<ApplicationServiceInvocationResolver>()
            .Resolve<TService>(operationName);
        var inboundPipeline = inboundScope.ServiceProvider.GetRequiredService<RequestInboundPipelineExecutor>();
        var execution = await inboundPipeline.ExecuteAsync(
            request, resolution.HandlerMapping.HandlerType, resolution.HandlerMapping.ReturnType,
            typeof(TService), operationName, null, cancellationToken, context).ConfigureAwait(false);
        return execution.Execution;
    }
}
