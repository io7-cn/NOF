using NHibernate.Mapping.ByCode;
using NOF.Hosting.AspNetCore.Extension.OidcServer;

namespace NOF.Hosting.AspNetCore.Extension.OidcServer.NHibernate;

/// <summary>Maps OIDC state using NHibernate's native mapping API.</summary>
public static class OidcModelConfigurations
{
    /// <summary>Configures clients, signing keys and revoked tokens.</summary>
    public static void Configure(ModelMapper mapper)
    {
        mapper.Class<OAuthClient>(map =>
        {
            map.Table(nameof(OAuthClient));
            map.Lazy(false);
            map.Id(value => value.ClientId, id => { id.Generator(Generators.Assigned); id.Length(128); });
            map.Property(value => value.DisplayName, property => { property.Length(256); property.NotNullable(true); });
            map.Property(value => value.SecretHash, property => { property.Length(128); property.NotNullable(true); });
            map.Property(value => value.SecretSalt, property => { property.Length(64); property.NotNullable(true); });
            map.Property(value => value.JsonWebKeySet, property => { property.Type(global::NHibernate.NHibernateUtil.StringClob); property.NotNullable(true); });
            map.Property(value => value.AllowedScopes, property => { property.Type(global::NHibernate.NHibernateUtil.StringClob); property.NotNullable(true); });
            map.Property(value => value.RedirectUris, property => { property.Type(global::NHibernate.NHibernateUtil.StringClob); property.NotNullable(true); });
            map.Property(value => value.AccessTokenClaims, property => { property.Type(global::NHibernate.NHibernateUtil.StringClob); property.NotNullable(true); });
            map.Property(value => value.TokenEndpointAuthenticationMethod, property => { property.Length(64); property.NotNullable(true); });
            map.Property(value => value.AllowedGrantTypes, property => { property.Type(global::NHibernate.NHibernateUtil.StringClob); property.NotNullable(true); });
            map.Property(value => value.AllowedResponseTypes, property => { property.Type(global::NHibernate.NHibernateUtil.StringClob); property.NotNullable(true); });
            map.Property(value => value.ApplicationType, property => { property.Length(16); property.NotNullable(true); });
            map.Property(value => value.RegistrationMetadata, property => { property.Type(global::NHibernate.NHibernateUtil.StringClob); property.NotNullable(true); });
            map.Property(value => value.RegistrationAccessTokenHash, property => { property.Length(128); property.NotNullable(true); });
            map.Property(value => value.RegistrationAccessTokenSalt, property => { property.Length(64); property.NotNullable(true); });
            map.Property(value => value.ClientType, property => { });
            map.Property(value => value.IsEnabled, property => { property.Column(column => column.Index("IX_OAuthClient_IsEnabled")); });
            map.Property(value => value.CreatedAtUtc, property => { });
            map.Property(value => value.UpdatedAtUtc, property => { });
        });
        mapper.Class<PersistedSigningKey>(map =>
        {
            map.Table(nameof(PersistedSigningKey));
            map.Lazy(false);
            map.Id(value => value.Kid, id => { id.Generator(Generators.Assigned); id.Length(64); });
            map.Property(value => value.EncryptedPrivateKey, property => { property.Type(global::NHibernate.NHibernateUtil.StringClob); property.NotNullable(true); });
            map.Property(value => value.PublicKey, property => { property.Type(global::NHibernate.NHibernateUtil.StringClob); property.NotNullable(true); });
            map.Property(value => value.Status, property => { property.Column(column => column.Index("IX_PersistedSigningKey_Status_CreatedAtUtc,IX_PersistedSigningKey_Status_InvalidatedAtUtc")); });
            map.Property(value => value.CreatedAtUtc, property => { property.Column(column => column.Index("IX_PersistedSigningKey_Status_CreatedAtUtc")); });
            map.Property(value => value.UpdatedAtUtc, property => { });
            map.Property(value => value.InvalidatedAtUtc, property => { property.Column(column => column.Index("IX_PersistedSigningKey_Status_InvalidatedAtUtc")); });
        });
        mapper.Class<RevokedRefreshToken>(map =>
        {
            map.Table(nameof(RevokedRefreshToken));
            map.Lazy(false);
            map.Id(value => value.TokenId, id => { id.Generator(Generators.Assigned); id.Length(256); });
            map.Property(value => value.ExpiresAtUtc, property => { property.Column(column => column.Index("IX_RevokedRefreshToken_ExpiresAtUtc")); });
        });
    }
}
