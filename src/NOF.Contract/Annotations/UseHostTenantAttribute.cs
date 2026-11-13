namespace NOF.Contract;

/// <summary>
/// Executes an application service operation in the host tenant after normal authorization succeeds.
/// Apply this attribute to the application service contract method. It does not grant permissions or bypass authorization.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class UseHostTenantAttribute : MetadataAttribute
{
    /// <summary>Gets the metadata key identifying host-tenant application service operations.</summary>
    public const string MetadataKey = "nof.tenant.use_host";

    /// <summary>Marks an application service contract method for host-tenant execution.</summary>
    public UseHostTenantAttribute()
        : base(MetadataKey, bool.TrueString)
    {
    }

}
