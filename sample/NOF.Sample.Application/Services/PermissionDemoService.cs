using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using System.Security.Claims;

namespace NOF.Sample.Application;

public partial class PermissionDemoService : ApplicationService<IPermissionDemoService>;

public sealed class GetPermissionOverview(PermissionCatalog permissions, IUserContext user) : PermissionDemoService.GetOverview
{
    public override Task<PermissionDemoOverview> HandleAsync(Empty request, InvocationContext context, CancellationToken cancellationToken)
        => Task.FromResult(new PermissionDemoOverview(user.User.Id, permissions.GetAll()));
}
