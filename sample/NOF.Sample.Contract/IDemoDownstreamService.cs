using NOF.Contract;
using System.ComponentModel;

namespace NOF.Sample;

public partial interface IDemoDownstreamService : IApplicationService
{
    [Summary("消费 access token")]
    [Description("由下游服务读取 access token 中的 subject、permissions、scopes 与 proxy service name")]
    [Category("认证演示")]
    [FailureCase("Rejected")]
    [FailureCase("Unauthorized", "401")]
    [FailureCase("Forbidden", "403")]
    [FailureCase("ServerError", "500")]
    ConsumeDemoAccessTokenResponse InspectAccessToken(Empty request);
}
