using NOF.Abstraction;
using NOF.Application;
using NOF.Domain;

namespace Microsoft.Extensions.DependencyInjection;
/// <summary>Explicit ambient convenience binding for independently created service scopes.</summary>
public static class AmbientBindingExtensions
{
    /// <summary>Binds this scope and semantic context to the current async flow; dispose to restore the caller.</summary>
    public static IDisposable BindNOF(this IServiceProvider services, Context context)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(context);
        return new NOFAmbientBinding(services, context);
    }
}
