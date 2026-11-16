using NOF.Contract;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace NOF.Application;

/// <summary>
/// Base type for source-generated application service containers.
/// </summary>
public abstract class ApplicationService;

/// <summary>
/// Typed base class for an application service container.
/// </summary>
public abstract class ApplicationService<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.Interfaces)] TService> : ApplicationService, IApplicationServiceType
    where TService : class, IApplicationService
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.Interfaces)]
    public static Type ServiceType => typeof(TService);
}
