using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;

namespace NOF.Infrastructure;

/// <summary>Creates isolated local execution scopes and copies only trusted caller identity.</summary>
public sealed class OperationInvoker(IServiceProvider services) : IOperationInvoker
{
    /// <inheritdoc />
    public async Task<ServiceExecution> InvokeAsync(ServiceOperationDescriptor operation, object input,
        Context context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.TryGetItem(NOFAbstractionConstants.Transport.Headers.TenantId, out _))
        {
            context = context.WithTenantId(Context.Current.TenantId);
        }
        var caller = services.GetRequiredService<IUserContext>();
        var identities = caller.User.Identities.Select(identity => identity.Clone()).ToArray();
        await using var scope = services.CreateAsyncScope();
        var user = scope.ServiceProvider.GetRequiredService<IUserContext>();
        if (!ReferenceEquals(caller, user))
        {
            user.Logout();
            user.User.AddIdentities(identities);
        }
        scope.ServiceProvider.ResolveDaemonServices();
        var result = await scope.ServiceProvider.GetRequiredService<IOperationExecutor>()
            .ExecuteAsync(operation, input, context, InvocationOrigin.Local, cancellationToken).ConfigureAwait(false);
        return result.Execution;
    }
}
