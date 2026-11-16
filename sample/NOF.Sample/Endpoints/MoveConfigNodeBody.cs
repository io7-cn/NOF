using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NOF.Abstraction;
using NOF.Contract;
using NOF.Hosting.AspNetCore;
using NOF.Sample.Application;

namespace NOF.Sample.Endpoints;

public sealed record MoveConfigNodeBody
{
    public required long? ParentId { get; init; }
}
