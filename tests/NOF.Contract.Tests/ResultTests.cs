using NOF.Abstraction;
using System.Text.Json;
using Xunit;

namespace NOF.Contract.Tests;

public class ResultTests
{
    [Fact]
    public void OrdinarySuccess_IsThePayloadItself()
    {
        Result<int> result = 42;
        Assert.IsType<int>(result.Value);
        Assert.Equal(42, result.ExpectSuccess());
        Assert.Equal("hello", Result.Success("hello").ExpectSuccess());
        Result<bool> falseValue = false;
        Assert.False(falseValue.ExpectSuccess());
    }

    [Fact]
    public void PayloadFreeSuccess_UsesExistingEmptyType_WithoutMetadata()
    {
        Result result = Empty.Instance;
        Assert.Same(Empty.Instance, result.ExpectSuccess());
        Assert.Same(Empty.Instance, Result.Success().ExpectSuccess());
        Assert.Null(typeof(Result).Assembly.GetType("NOF.Contract.Success"));
        Assert.Null(typeof(Result).Assembly.GetType("NOF.Contract.Success`1"));
        Assert.DoesNotContain(typeof(Result).GetMethods(), method => method.Name == "WithMessage");
    }

    [Fact]
    public void Failure_IsOneSharedAbstractionType()
    {
        var failure = new Failure("not_found", "Missing");
        Result untyped = failure;
        Result<int> typed = failure;
        Assert.Same(failure, untyped.ExpectFailure());
        Assert.Same(failure, typed.ExpectFailure());
        Assert.Same(failure, Result.Fail(failure));
        Assert.Equal("NOF.Abstraction", typeof(Failure).Assembly.GetName().Name);
        Assert.Null(typeof(Result).Assembly.GetType("NOF.Contract.Failure"));
    }

    [Fact]
    public void Construction_RejectsMissingDataAndFailureDetails()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Success<string>(null!));
        Assert.Throws<ArgumentException>(() => new Failure("", "Missing"));
        Assert.Throws<ArgumentNullException>(() => new Failure("invalid", null!));
    }

    [Fact]
    public void Page_IsAnOrdinaryTypedPayload()
    {
        var page = Result.Page([1, 2], 5).ExpectSuccess();
        Assert.Equal([1, 2], page.Items);
        Assert.Equal(5, page.TotalCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Page<int>([], -1));
    }

    [Fact]
    public async Task Stream_IsAnOrdinaryTypedPayload()
    {
        var stream = Items();
        var bare = Result.Stream(stream);
        Assert.Same(stream, bare.ExpectSuccess());
        var items = new List<int>();
        await foreach (var item in bare.ExpectSuccess())
        {
            items.Add(item);
        }
        Assert.Equal([1, 2], items);
    }

    [Fact]
    public void Json_PlainSuccess_DoesNotAddAWrapper()
    {
        Assert.Equal("42", JsonSerializer.Serialize<Result<int>>(42, JsonSerializerOptions.NOF));
        Assert.Equal(42, JsonSerializer.Deserialize<Result<int>>("42", JsonSerializerOptions.NOF).ExpectSuccess());
        var value = new Dictionary<string, int> { ["answer"] = 42 };
        var json = JsonSerializer.Serialize(Result.Success(value), JsonSerializerOptions.NOF);
        Assert.Equal("{\"answer\":42}", json);
        Assert.Equal(42, JsonSerializer.Deserialize<Result<Dictionary<string, int>>>(json, JsonSerializerOptions.NOF).ExpectSuccess()["answer"]);
    }

    [Theory]
    [InlineData("Saved")]
    [InlineData("")]
    [InlineData("保存成功\r\nMore detail")]
    public void Json_BusinessDto_OwnsItsSuccessfulMessage(string message)
    {
        var json = JsonSerializer.Serialize(Result.Success(new ImportReceipt(42, message)), JsonSerializerOptions.NOF);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(42, document.RootElement.GetProperty("importedCount").GetInt32());
        Assert.Equal(message, document.RootElement.GetProperty("message").GetString());
        Assert.False(document.RootElement.TryGetProperty("kind", out _));
        var success = JsonSerializer.Deserialize<Result<ImportReceipt>>(json, JsonSerializerOptions.NOF).ExpectSuccess();
        Assert.Equal(42, success.ImportedCount);
        Assert.Equal(message, success.Message);
    }

    [Fact]
    public void Json_Failure_RoundTripsAcrossUnrelatedPayloadTypes()
    {
        Result<int> result = Result.Fail("not_found", "Missing");
        var json = JsonSerializer.Serialize(result, JsonSerializerOptions.NOF);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("failure", document.RootElement.GetProperty("$kind").GetString());
        Assert.Equal("not_found", document.RootElement.GetProperty("code").GetString());
        Assert.Equal(result.ExpectFailure(), JsonSerializer.Deserialize<Result<Dictionary<string, string>>>(json, JsonSerializerOptions.NOF).ExpectFailure());
        Assert.Equal(result.ExpectFailure(), JsonSerializer.Deserialize<Result>(json, JsonSerializerOptions.NOF).ExpectFailure());
    }

    [Fact]
    public void Json_CodeAndMessageInPayload_DoNotBecomeFailure()
    {
        const string json = """{"code":"abc","message":"A successful DTO"}""";
        var value = JsonSerializer.Deserialize<Result<Dictionary<string, string>>>(json, JsonSerializerOptions.NOF).ExpectSuccess();
        Assert.Equal("abc", value["code"]);
    }

    [Theory]
    [InlineData("{\"$kind\":\"success\",\"code\":\"bad\",\"message\":\"Invalid\"}")]
    [InlineData("{\"$kind\":42,\"code\":\"bad\",\"message\":\"Invalid\"}")]
    [InlineData("{\"$kind\":\"failure\",\"code\":\"bad\",\"message\":\"Invalid\",\"data\":42}")]
    public void Json_InvalidFailureMarkerOrMixedCases_AreRejected(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Result<int>>(json, JsonSerializerOptions.NOF));
    }

    [Fact]
    public void Json_SourceGeneratedMetadata_SupportsEmptyBusinessDtoPageAndFailureCases()
    {
        var context = new ResultTestJsonContext(new JsonSerializerOptions(JsonSerializerOptions.NOF));
        var empty = JsonSerializer.Serialize(Result.Success(), context.Result);
        Assert.Equal("{}", empty);
        Assert.IsType<Empty>(JsonSerializer.Deserialize(empty, context.Result).Value);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Result>("{\"message\":\"Saved\"}", context.Result));
        var plain = JsonSerializer.Serialize<Result<int>>(42, context.ResultInt32);
        Assert.Equal(42, JsonSerializer.Deserialize(plain, context.ResultInt32).ExpectSuccess());
        var receipt = JsonSerializer.Serialize(Result.Success(new ImportReceipt(42, "Loaded")), context.ResultImportReceipt);
        Assert.Equal("Loaded", JsonSerializer.Deserialize(receipt, context.ResultImportReceipt).ExpectSuccess().Message);
        var page = JsonSerializer.Serialize(Result.Page([1, 2], 5), context.ResultPageInt32);
        Assert.Equal(5, JsonSerializer.Deserialize(page, context.ResultPageInt32).ExpectSuccess().TotalCount);
        var failureJson = JsonSerializer.Serialize<Result<int>>(Result.Fail("bad", "Invalid"), context.ResultInt32);
        Assert.Equal("bad", JsonSerializer.Deserialize(failureJson, context.ResultInt32).ExpectFailure().Code);
    }

    private static async IAsyncEnumerable<int> Items()
    {
        await Task.Yield();
        yield return 1;
        yield return 2;
    }
}
