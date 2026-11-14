using NOF.Infrastructure;

namespace NOF.Hosting.AspNetCore.Extension.OidcServer;

public enum PersistedSigningKeyStatus
{
    Active = 1,
    Retired = 2,
    Revoked = 3,
    NextActive = 4
}

public sealed class PersistedSigningKey
{
    public string Kid { get; set; } = string.Empty;

    public string EncryptedPrivateKey { get; set; } = string.Empty;

    public string PublicKey { get; set; } = string.Empty;

    public PersistedSigningKeyStatus Status { get; set; } = PersistedSigningKeyStatus.Active;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? InvalidatedAtUtc { get; set; }
}
