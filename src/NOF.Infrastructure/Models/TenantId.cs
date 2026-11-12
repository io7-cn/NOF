using NOF.Abstraction;
using NOF.Domain;

namespace NOF.Infrastructure;

[ValueObjectLength(256, MinimumLength = 1)]
public readonly partial struct TenantId : IValueObject<string>
{
    public static TenantId Host { get; } = From(NOFAbstractionConstants.Tenant.HostId);

    public static string Normalize(string? tenantId)
    {
        return NOFAbstractionConstants.Tenant.NormalizeTenantId(tenantId).Trim();
    }

    public static bool IsValid(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value[0] is < 'a' or > 'z')
        {
            return false;
        }
        foreach (var character in value)
        {
            if ((character < 'a' || character > 'z') && (character < '0' || character > '9'))
            {
                return false;
            }
        }
        return true;
    }
}
