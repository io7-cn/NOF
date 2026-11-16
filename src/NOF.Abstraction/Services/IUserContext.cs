using System.Security.Claims;

namespace NOF.Abstraction;

/// <summary>
/// 表示当前逻辑执行上下文中的用户信息。
/// </summary>
public interface IUserContext
{
    /// <summary>
    /// 当前用户的固定主体；外部仅应通过增删 Identity 修改用户状态。
    /// </summary>
    ClaimsPrincipal User { get; }

    /// <summary>
    /// 退出当前登录状态并清空全部身份。
    /// </summary>
    void Logout();

    /// <summary>用户状态变更前触发。</summary>
    event Action? StateChanging;

    /// <summary>用户状态变更后触发。</summary>
    event Action? StateChanged;
}
