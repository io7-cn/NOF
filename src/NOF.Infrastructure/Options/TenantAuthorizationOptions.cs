namespace NOF.Infrastructure;

/// <summary>Configures tenant trust checks for application operation execution.</summary>
public class TenantAuthorizationOptions
{
    /// <summary>
    /// Requires an authenticated identity with a non-host tenant for ordinary application operations.
    /// Host-tenant operations require authentication but may omit an identity tenant;
    /// they must declare their own platform permission requirements.
    /// Defaults to false for compatibility with single-tenant and header-based hosts.
    /// Applies to local, HTTP and message origins alike.
    /// </summary>
    public bool RequireAuthenticatedTenant { get; set; }

    /// <summary>
    /// Rejects a non-host request tenant that differs from the authenticated identity tenant.
    /// Missing or host tenant headers allow selection from the verified identity.
    /// Explicit host-tenant operations are exempt because they intentionally change tenancy.
    /// Defaults to false. Does not apply to command or notification consumers.
    /// </summary>
    public bool RejectMismatchedTenant { get; set; }
}
