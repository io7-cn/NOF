using Microsoft.EntityFrameworkCore;
using NOF.Hosting.AspNetCore.Extension.OidcServer;
using NOF.Infrastructure.EntityFrameworkCore;

namespace NOF.Hosting.AspNetCore.Extension.OidcServer.EntityFrameworkCore;

/// <summary>Configures framework-owned entities with native EF Core APIs.</summary>
public sealed class OAuthClientModelCreatingContributor : IEFCoreModelConfiguration
{
    /// <inheritdoc />
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OAuthClient>(entity =>
        {
            entity.ToTable(nameof(OAuthClient));
            entity.HasKey(e => e.ClientId);
            entity.HasIndex(e => e.IsEnabled);
            entity.Property(e => e.ClientId).HasMaxLength(128).IsRequired();
            entity.Property(e => e.DisplayName).HasMaxLength(256).IsRequired();
            entity.Property(e => e.SecretHash).HasMaxLength(128).IsRequired();
            entity.Property(e => e.SecretSalt).HasMaxLength(64).IsRequired();
            entity.Property(e => e.JsonWebKeySet).IsRequired();
            entity.Property(e => e.AllowedScopes).IsRequired();
            entity.Property(e => e.RedirectUris).IsRequired();
            entity.Property(e => e.AccessTokenClaims).IsRequired();
            entity.Property(e => e.TokenEndpointAuthenticationMethod).HasMaxLength(64).IsRequired();
            entity.Property(e => e.AllowedGrantTypes).IsRequired();
            entity.Property(e => e.AllowedResponseTypes).IsRequired();
            entity.Property(e => e.ApplicationType).HasMaxLength(16).IsRequired();
            entity.Property(e => e.RegistrationMetadata).IsRequired();
            entity.Property(e => e.RegistrationAccessTokenHash).HasMaxLength(128).IsRequired();
            entity.Property(e => e.RegistrationAccessTokenSalt).HasMaxLength(64).IsRequired();
            entity.Property(e => e.ClientType).IsRequired();
            entity.Property(e => e.IsEnabled).IsRequired();
            entity.Property(e => e.CreatedAtUtc).IsRequired();
            entity.Property(e => e.UpdatedAtUtc).IsRequired();
        });
    }
}

/// <summary>Configures framework-owned entities with native EF Core APIs.</summary>
public sealed class PersistedSigningKeyModelCreatingContributor : IEFCoreModelConfiguration
{
    /// <inheritdoc />
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PersistedSigningKey>(entity =>
        {
            entity.ToTable(nameof(PersistedSigningKey));
            entity.HasKey(e => e.Kid);
            entity.HasIndex(e => new { e.Status, e.CreatedAtUtc });
            entity.HasIndex(e => new { e.Status, e.InvalidatedAtUtc });
            entity.Property(e => e.Kid).HasMaxLength(64).IsRequired();
            entity.Property(e => e.EncryptedPrivateKey).IsRequired();
            entity.Property(e => e.PublicKey).IsRequired();
            entity.Property(e => e.Status).IsRequired();
            entity.Property(e => e.CreatedAtUtc).IsRequired();
            entity.Property(e => e.UpdatedAtUtc).IsRequired();
        });
    }
}

/// <summary>Configures framework-owned entities with native EF Core APIs.</summary>
public sealed class RevokedRefreshTokenModelCreatingContributor : IEFCoreModelConfiguration
{
    /// <inheritdoc />
    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RevokedRefreshToken>(entity =>
        {
            entity.ToTable(nameof(RevokedRefreshToken));
            entity.HasKey(e => e.TokenId);
            entity.HasIndex(e => e.ExpiresAtUtc);
            entity.Property(e => e.TokenId).HasMaxLength(256).IsRequired();
            entity.Property(e => e.ExpiresAtUtc).IsRequired();
        });
    }
}
