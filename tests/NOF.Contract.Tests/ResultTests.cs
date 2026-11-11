using System.Text.Json;
using Xunit;

namespace NOF.Contract.Tests;

public class ResultTests
{
    [Fact]
    public void Success_CarriesMessageWithOrWithoutData()
    {
        Result result = new Success("Saved");
        Assert.Equal("Saved", result.ExpectSuccess().Message);
        Assert.Empty(Result.Success().ExpectSuccess().Message);
        Result<string> typed = new Success<string>("hello", "Loaded");
        var (data, message) = typed.ExpectSuccess();
        Assert.Equal(("hello", "Loaded"), (data, message));
        Assert.Equal(42, Result.Success(42).ExpectSuccess().Data);
    }

    [Fact]
    public void Failure_IsSharedByTypedAndUntypedResults()
    {
        var failure = Result.Fail("not_found", "Missing");
        Result untyped = failure;
        Result<int> typed = failure;
        Assert.Same(failure, untyped.ExpectFailure());
        Assert.Same(failure, typed.ExpectFailure());
        Assert.Equal("not_found", failure.Code);
        Assert.Equal("Missing", failure.Message);
    }

    [Fact]
    public void ImplicitPayload_CreatesTypedSuccess()
    {
        Result<string> result = "hello";
        Assert.Equal("hello", result.ExpectSuccess().Data);
    }

    [Fact]
    public void Construction_RejectsMissingPayloadOrError()
    {
        Assert.Throws<ArgumentNullException>(() => new Success<string>(null!));
        Assert.Throws<ArgumentNullException>(() => new Success(null!));
        Assert.Throws<ArgumentNullException>(() => new Failure((ResultError)null!));
        Assert.Throws<ArgumentException>(() => new Failure("", "Missing"));
        Assert.Throws<ArgumentNullException>(() => new Failure("invalid", null!));
    }

    [Fact]
    public void Page_HasTypedItemsAndCount()
    {
        var success = Result.Page([1, 2], 5, "Page loaded").ExpectSuccess();
        Assert.Equal([1, 2], success.Data.Items);
        Assert.Equal(5, success.Data.TotalCount);
        Assert.Equal("Page loaded", success.Message);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Page<int>([], -1));
    }

    [Fact]
    public async Task Stream_PreservesMessageAndTypedItems()
    {
        var success = Result.Stream(Items(), "Stream opened").ExpectSuccess();
        Assert.Equal("Stream opened", success.Message);
        var items = new List<int>();
        await foreach (var item in success.Data)
        {
            items.Add(item);
        }
        Assert.Equal([1, 2], items);
    }

    [Fact]
    public void TransportBoundary_PropagatesFailureWithoutInventingPayload()
    {
        Result result = Result.Fail("forbidden", "Denied");
        Assert.Same(result.ExpectFailure(), RpcResponse.Require<Result<string>>(result).ExpectFailure());
        Assert.Throws<InvalidOperationException>(() => RpcResponse.Require<Result<string>>(Result.Success()));
        Assert.Throws<InvalidOperationException>(() => RpcResponse.Require<Result<string>>(default(Result<string>)));
        Assert.Throws<InvalidOperationException>(() => Result<int>.CreateEmptyResponse());
    }

    [Theory]
    [InlineData("Saved")]
    [InlineData("")]
    [InlineData("保存成功\r\nMore detail")]
    public void Json_SuccessWithMessage_RoundTrips(string message)
    {
        var json = JsonSerializer.Serialize(Result.Success(42, message), JsonSerializerOptions.NOF);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("success", document.RootElement.GetProperty("kind").GetString());
        Assert.Equal(42, document.RootElement.GetProperty("data").GetInt32());
        Assert.Equal(message, document.RootElement.GetProperty("message").GetString());
        Assert.False(document.RootElement.TryGetProperty("error", out _));
        var success = JsonSerializer.Deserialize<Result<int>>(json, JsonSerializerOptions.NOF).ExpectSuccess();
        Assert.Equal(42, success.Data);
        Assert.Equal(message, success.Message);
    }

    [Fact]
    public void Json_UntypedSuccess_RoundTrips()
    {
        var json = JsonSerializer.Serialize(Result.Success("Done"), JsonSerializerOptions.NOF);
        Assert.Equal("Done", JsonSerializer.Deserialize<Result>(json, JsonSerializerOptions.NOF).ExpectSuccess().Message);
    }

    [Fact]
    public void Json_Failure_RoundTripsAcrossPayloadTypes()
    {
        Result<int> result = Result.Fail("not_found", "Missing");
        var json = JsonSerializer.Serialize(result, JsonSerializerOptions.NOF);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("failure", document.RootElement.GetProperty("kind").GetString());
        Assert.Equal("not_found", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.False(document.RootElement.TryGetProperty("data", out _));
        Assert.Equal(result.ExpectFailure(), JsonSerializer.Deserialize<Result<string>>(json, JsonSerializerOptions.NOF).ExpectFailure());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"kind\":\"success\"}")]
    [InlineData("{\"data\":42}")]
    [InlineData("{\"kind\":\"failure\",\"data\":42}")]
    [InlineData("{\"kind\":\"success\",\"error\":{\"code\":\"bad\",\"message\":\"Missing\"}}")]
    [InlineData("{\"kind\":\"success\",\"data\":42,\"error\":{\"code\":\"bad\",\"message\":\"Missing\"}}")]
    [InlineData("{\"isSuccess\":true,\"value\":42}")]
    public void Json_RejectsMissingContradictoryOrLegacyCases(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Result<int>>(json, JsonSerializerOptions.NOF));
    }

    [Fact]
    public void Json_SourceGeneratedMetadata_PreservesCasesAndMessages()
    {
        var context = new ResultTestJsonContext(new JsonSerializerOptions(JsonSerializerOptions.NOF));
        var json = JsonSerializer.Serialize(Result.Success(42, "Loaded"), context.ResultInt32);
        Assert.Equal("Loaded", JsonSerializer.Deserialize(json, context.ResultInt32).ExpectSuccess().Message);
        var failureJson = JsonSerializer.Serialize<Result<int>>(Result.Fail("bad", "Invalid"), context.ResultInt32);
        Assert.Equal("bad", JsonSerializer.Deserialize(failureJson, context.ResultInt32).ExpectFailure().Code);
    }

    [Fact]
    public void PublicApi_DoesNotExposeLegacyStateOrReflectionProjection()
    {
        Assert.Null(typeof(Result<int>).GetProperty("IsSuccess"));
        Assert.Null(typeof(Result<int>).GetProperty("ErrorCode"));
        Assert.Null(typeof(Result<int>).GetProperty("Extra"));
        Assert.Null(typeof(Result<int>).Assembly.GetType("NOF.Contract.IResult"));
        Assert.Null(typeof(Result<int>).Assembly.GetType("NOF.Contract.ResultProjection"));
    }

    private static async IAsyncEnumerable<int> Items()
    {
        await Task.Yield();
        yield return 1;
        yield return 2;
    }
}
