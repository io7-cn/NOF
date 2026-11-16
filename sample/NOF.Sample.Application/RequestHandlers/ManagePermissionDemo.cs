using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using System.Security.Claims;

namespace NOF.Sample.Application;

public sealed class ManagePermissionDemo(IUserContext user) : PermissionDemoService.Manage
{
    public override Task<PermissionDemoOverview> HandleAsync(Empty request, InvocationContext context, CancellationToken cancellationToken)
        => Task.FromResult(new PermissionDemoOverview(user.User.Id, SamplePermissions.Definitions));
}
