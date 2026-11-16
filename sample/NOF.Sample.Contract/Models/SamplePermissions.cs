using NOF.Abstraction;
using NOF.Contract;

namespace NOF.Sample;

public static class SamplePermissions
{
    public const string ViewDirectory = "sample.permissions.read";
    public const string ManageDirectory = "sample.permissions.manage";
    public static SamplePermission[] Definitions =>
    [
        new(ViewDirectory, "查看权限目录", "权限演示"),
        new(ManageDirectory, "管理权限目录", "权限演示")
    ];
}
