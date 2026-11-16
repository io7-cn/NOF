using System.Security.Claims;

namespace NOF.Abstraction;

/// <summary>
/// 默认的 <see cref="IUserContext"/> 实现。
/// </summary>
public sealed class UserContext : IUserContext
{
    /// <inheritdoc />
    public event Action? StateChanging;

    /// <inheritdoc />
    public event Action? StateChanged;

    /// <inheritdoc />
    public ClaimsPrincipal User { get; private set; }

    public UserContext()
    {
        User = CreateUser();
    }

    /// <inheritdoc />
    public void Logout()
    {
        StateChanging?.Invoke();
        User = CreateUser();
        StateChanged?.Invoke();
    }

    private UserClaimsPrincipal CreateUser()
    {
        return new UserClaimsPrincipal(
            () => StateChanging?.Invoke(),
            () => StateChanged?.Invoke());
    }
}
