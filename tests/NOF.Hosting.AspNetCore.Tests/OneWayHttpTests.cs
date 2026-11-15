using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using Xunit;

namespace NOF.Hosting.AspNetCore.Tests;

public sealed class OneWayHttpTests
{
    [Fact]
    public async Task ReplyMapping_CannotPretendAOneWayOperationReturnsAnHttpResponse()
    {
        var builder = WebApplication.CreateBuilder().AddNOF();
        builder.Services.GetOrAddSingleton<ApplicationServiceRegistry>().AddOperation(new(typeof(Send), typeof(Message), typeof(void))
        {
            Descriptor = new OperationDescriptor<Message>(typeof(Send))
        });
        await using var app = builder.Build();
        var error = Assert.Throws<InvalidOperationException>(() => app.MapPost<Send>("/send"));
        Assert.Contains("acceptance", error.Message);
    }

    public sealed record Message;
    public interface IService : IApplicationService
    {
        void Send(Message request);
    }
    public abstract class Send : OperationHandler<Message>
    {
    }
}
