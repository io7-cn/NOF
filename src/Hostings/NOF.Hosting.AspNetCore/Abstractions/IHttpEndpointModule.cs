using Microsoft.AspNetCore.Routing;

namespace NOF.Hosting.AspNetCore;

/// <summary>Groups native ASP.NET Core endpoints owned by one API feature.</summary>
/// <remarks>Register as a singleton, or use AutoInject and add its assembly as an application part.</remarks>
public interface IHttpEndpointModule
{
    /// <summary>Maps the module's routes using native ASP.NET Core endpoint APIs.</summary>
    void Map(IEndpointRouteBuilder endpoints);
}
