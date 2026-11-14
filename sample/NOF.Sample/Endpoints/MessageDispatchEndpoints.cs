using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using NOF.Hosting.AspNetCore;
using NOF.Sample.Application;

namespace NOF.Sample.Endpoints;

public static class MessageDispatchEndpoints
{
    public static RouteGroupBuilder MapMessageDispatchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/messages").WithTags("RabbitMQ dispatch demo");
        group.MapPost<MessageDispatchService.SubmitOrder>("/orders")
            .Response<SubmittedDemoOrder>(StatusCodes.Status202Accepted)
            .Response<MessageDispatchService.SubmitOrder.InvalidRecipient>(StatusCodes.Status400BadRequest);
        return group;
    }
}
