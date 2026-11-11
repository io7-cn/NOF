using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NOF.Contract;
using System.Text;
using System.Text.Json;
using Xunit;

namespace NOF.Hosting.AspNetCore.Tests;

public class StreamingResultUnionTests
{
    [Fact]
    public async Task Sse_Success_PreservesUnicodeMessageAndItems()
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        using var body = new MemoryStream();
        context.Response.Body = body;
        await new RpcStreamingHttpResult<int>(Result.Stream(Items(), "准备好了\r\nStream opened")).ExecuteAsync(context);

        Assert.Equal("text/event-stream", context.Response.ContentType);
        Assert.Contains("data: 42", Encoding.UTF8.GetString(body.ToArray()));
        using var response = new HttpResponseMessage();
        response.Headers.Add(StreamingResponseMetadata.MessageHeader, context.Response.Headers[StreamingResponseMetadata.MessageHeader].ToString());
        Assert.Equal("准备好了\r\nStream opened", StreamingResponseMetadata.ReadMessage(response));
    }

    [Fact]
    public async Task Sse_InitialFailure_UsesFailureCaseJson()
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        using var body = new MemoryStream();
        context.Response.Body = body;
        Result<IAsyncEnumerable<int>> result = Result.Fail("stream.denied", "Denied");
        await new RpcStreamingHttpResult<int>(result).ExecuteAsync(context);
        var failure = JsonSerializer.Deserialize<Result>(body.ToArray(), JsonSerializerOptions.NOF).ExpectFailure();
        Assert.Equal("stream.denied", failure.Code);
    }

    [Fact]
    public async Task Sse_UninitializedResult_IsRejected()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => new RpcStreamingHttpResult<int>(default).ExecuteAsync(new DefaultHttpContext()));
    }

    private static async IAsyncEnumerable<int> Items()
    {
        await Task.Yield();
        yield return 42;
    }
}
