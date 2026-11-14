# NOF.Hosting.AspNetCore.Extension.OidcServer.EntityFrameworkCore

Native EntityFrameworkCore state storage for the NOF OIDC server. Install this package alongside
`NOF.Hosting.AspNetCore.Extension.OidcServer` and configure the corresponding native persistence provider.

```csharp
builder.Services.AddOidcEntityFrameworkCore();
```

Call `AddOidcServer` as usual. Client management, signing-key rotation and refresh-token revocation
share the provider's scoped unit of work; protocol policy remains in the base OIDC extension.

See [native persistence and migration](https://github.com/io7-cn/NOF/blob/main/docs/en/design/persistence-boundaries.md)
and its [Chinese edition](https://github.com/io7-cn/NOF/blob/main/docs/zh-CN/design/persistence-boundaries.md).
