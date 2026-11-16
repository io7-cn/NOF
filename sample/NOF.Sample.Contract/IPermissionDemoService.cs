using NOF.Abstraction;
using NOF.Contract;

namespace NOF.Sample;

public interface IPermissionDemoService : IApplicationService
{
    [RequirePermission(SamplePermissions.ViewDirectory, SamplePermissions.ManageDirectory, Match = PermissionMatch.Any)]
    PermissionDemoOverview GetOverview(Empty request);

    [RequirePermission(SamplePermissions.ManageDirectory)]
    PermissionDemoOverview Manage(Empty request);
}
