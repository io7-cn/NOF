using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using System.Reflection;
using System.Security.Claims;

namespace NOF.Infrastructure;

public interface IInboundAuthorizationHandler
{
    ValueTask<InboundAuthorizationResult> AuthorizeAsync(
        InboundAuthorizationContext context,
        CancellationToken cancellationToken);
}
