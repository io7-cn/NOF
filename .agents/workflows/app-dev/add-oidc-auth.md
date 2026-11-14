---
description: Integrate native JwtBearer and an upstream OAuth/OIDC identity provider with NOF
---

# Add native identity

Use the native host APIs. NOF does not own an OIDC Server package, JWT validator, discovery/JWKS cache or endpoint-interface hierarchy.

1. Choose an external identity provider or install OpenIddict directly into the host.
2. Configure native storage and the provider's login, consent, client/permission management and durable credentials. The Sample integrates native OpenIddict models in its existing ConfigurationDbContext and migration chain; business soft-delete conventions are disabled for those entities.
3. Register AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer with Authority, Audience and MapInboundClaims=false. Keep issuer/audience/lifetime/signature validation and HTTPS metadata enabled for deployment.
4. Call UseAuthentication and UseAuthorization before UseNOF; use native RequireAuthorization for native challenge/forbid responses. NOF copies verified HttpContext.User into IUserContext and continues cross-origin business authorization/tenant checks.
5. Application-owned filters may select execution context. Domain needs no registration, IdGenerator.Current is nullable, and Snowflake is an explicit Infrastructure provider.
6. Configure source-generated JSON metadata per host and native telemetry explicitly.

Use docs/en/design/host-composition.md (and its reciprocal Chinese edition) for the complete API and migration. Old OAuth tables, credentials and sessions are not automatically compatible with OpenIddict. Review client provisioning, rotate credentials/re-authenticate and retain backups. No migrations are applied simply to configure or test source code.

Official references: https://documentation.openiddict.com/introduction and https://learn.microsoft.com/aspnet/core/security/authentication/configure-jwt-bearer-authentication.
