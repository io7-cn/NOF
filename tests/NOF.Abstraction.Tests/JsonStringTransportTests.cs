using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace NOF.Abstraction.Tests;

public sealed class JsonStringTransportTests
{
    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    [InlineData(9007199254740993L)]
    [InlineData(0L)]
    public void Int64RoundTripsWithoutPrecisionLoss(long value)
    {
        var options = JsonSerializerOptions.CreateNOF().UseStringInt64();
        Assert.Equal($"\"{value}\"", JsonSerializer.Serialize(value, options.GetRequiredTypeInfo<long>()));
        Assert.Equal(value, JsonSerializer.Deserialize($"\"{value}\"", options.GetRequiredTypeInfo<long>()));
        Assert.Equal(value, JsonSerializer.Deserialize(value.ToString(System.Globalization.CultureInfo.InvariantCulture), options.GetRequiredTypeInfo<long>()));
    }

    [Theory]
    [InlineData("\"9223372036854775808\"")]
    [InlineData("\"-9223372036854775809\"")]
    [InlineData("\"1.5\"")]
    [InlineData("\"1e3\"")]
    [InlineData("\" 1\"")]
    [InlineData("1.5")]
    [InlineData("true")]
    [InlineData("null")]
    public void Int64RejectsInvalidInput(string json)
    {
        var options = JsonSerializerOptions.CreateNOF().UseStringInt64();
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, options.GetRequiredTypeInfo<long>()));
    }

    [Fact]
    public void OptionsHandleNullableCollectionsAndDictionaryKeysAndKeepDefaultsIsolated()
    {
        var options = JsonSerializerOptions.CreateNOF().UseStringInt64().UseStringInt64().UseStringEnums<State>();
        options.TypeInfoResolverChain.Insert(0, TransportJsonContext.Default);
        var payload = new Payload { Id = long.MaxValue, State = State.InProgress, Values = [long.MinValue, null], States = [State.Ready], Count = 7 };
        var json = JsonSerializer.Serialize(payload, options.GetRequiredTypeInfo<Payload>());
        using var document = JsonDocument.Parse(json);
        Assert.Equal(long.MaxValue.ToString(), document.RootElement.GetProperty("id").GetString());
        Assert.Equal("in-progress", document.RootElement.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Number, document.RootElement.GetProperty("count").ValueKind);
        var restored = JsonSerializer.Deserialize(json, options.GetRequiredTypeInfo<Payload>())!;
        Assert.Equal(payload.Values, restored.Values);
        Assert.Equal(payload.States, restored.States);
        Assert.Equal("null", JsonSerializer.Serialize((long?)null, options.GetRequiredTypeInfo<long?>()));
        var keys = new Dictionary<long, State> { [long.MaxValue] = State.Ready };
        var dictionaryJson = JsonSerializer.Serialize(keys, options.GetRequiredTypeInfo<Dictionary<long, State>>());
        Assert.Equal(State.Ready, JsonSerializer.Deserialize(dictionaryJson, options.GetRequiredTypeInfo<Dictionary<long, State>>())![long.MaxValue]);
        var defaults = JsonSerializerOptions.CreateNOF();
        Assert.Equal("1", JsonSerializer.Serialize(1L, defaults.GetRequiredTypeInfo<long>()));
        Assert.Equal("0", JsonSerializer.Serialize(State.Ready, defaults.GetRequiredTypeInfo<State>()));
        Assert.Throws<InvalidOperationException>(() => options.UseStringEnums<State>());
    }

    [Fact]
    public void EnumsHonorWireNamesNamingPolicyFlagsAndStrictIntegerHandling()
    {
        var options = JsonSerializerOptions.CreateNOF().UseStringEnums<State>(JsonNamingPolicy.CamelCase).UseStringEnums<Access>();
        Assert.Equal("\"ready\"", JsonSerializer.Serialize(State.Ready, options.GetRequiredTypeInfo<State>()));
        Assert.Equal(State.InProgress, JsonSerializer.Deserialize("\"in-progress\"", options.GetRequiredTypeInfo<State>()));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("1", options.GetRequiredTypeInfo<State>()));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("\"1\"", options.GetRequiredTypeInfo<State>()));
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize((State)99, options.GetRequiredTypeInfo<State>()));
        var flags = Access.Read | Access.Write;
        Assert.Equal(flags, JsonSerializer.Deserialize(JsonSerializer.Serialize(flags, options.GetRequiredTypeInfo<Access>()), options.GetRequiredTypeInfo<Access>()));
        var permissive = JsonSerializerOptions.CreateNOF().UseStringEnums(allowIntegerValues: true);
        Assert.Equal((State)99, JsonSerializer.Deserialize("99", permissive.GetRequiredTypeInfo<State>()));
    }

    public enum State
    {
        Ready,
        [JsonStringEnumMemberName("in-progress")]
        InProgress
    }

    [Flags]
    public enum Access { Read = 1, Write = 2 }

    public sealed record Payload
    {
        public long Id { get; init; }
        public State? State { get; init; }
        public long?[] Values { get; init; } = [];
        public State[] States { get; init; } = [];
        public int Count { get; init; }
    }
}

[JsonSerializable(typeof(JsonStringTransportTests.Payload))]
[JsonSerializable(typeof(Dictionary<long, JsonStringTransportTests.State>))]
public partial class TransportJsonContext : JsonSerializerContext;
