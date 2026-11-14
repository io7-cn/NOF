using NOF.Infrastructure;

namespace NOF.Hosting.AspNetCore.Extension.OidcServer;

public sealed class RevokedRefreshToken
{
    public string TokenId { get; set; } = string.Empty;

    public DateTime ExpiresAtUtc { get; set; }
}
