using NOF.Contract;

namespace NOF.Sample;

public static class SamplePermissions
{
    public const string ViewDirectory = "sample.permissions.read";
    public const string ManageDirectory = "sample.permissions.manage";
    public static PermissionDefinition[] Definitions =>
    [
        new(ViewDirectory, "查看权限目录", "权限演示"),
        new(ManageDirectory, "管理权限目录", "权限演示")
    ];
}

public interface IPermissionDemoService : IApplicationService
{
    [RequirePermission(SamplePermissions.ViewDirectory, SamplePermissions.ManageDirectory, Match = PermissionMatch.Any)]
    PermissionDemoOverview GetOverview(Empty request);
}

public sealed record PermissionDemoOverview(string? UserId, IReadOnlyList<PermissionDefinition> Permissions);
