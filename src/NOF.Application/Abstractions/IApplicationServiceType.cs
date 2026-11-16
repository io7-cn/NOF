using NOF.Contract;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace NOF.Application;

[EditorBrowsable(EditorBrowsableState.Never)]
public interface IApplicationServiceType
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.Interfaces)]
    static abstract Type ServiceType { get; }
}
