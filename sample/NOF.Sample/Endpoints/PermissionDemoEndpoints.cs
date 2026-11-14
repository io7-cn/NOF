using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NOF.Hosting.AspNetCore;
using NOF.Sample.Application;

namespace NOF.Sample.Endpoints;

public static class PermissionDemoEndpoints
{
    public static void MapPermissionDemoEndpoints(this IEndpointRouteBuilder endpoints)
        => endpoints.MapGet<PermissionDemoService.GetOverview>("/api/permissions/overview").WithTags("Permissions");
}
