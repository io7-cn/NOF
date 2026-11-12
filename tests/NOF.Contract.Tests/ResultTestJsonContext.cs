using System.Text.Json.Serialization;

namespace NOF.Contract.Tests;

[JsonSerializable(typeof(Result<int>))]
[JsonSerializable(typeof(Result))]
[JsonSerializable(typeof(Result<ImportReceipt>))]
[JsonSerializable(typeof(Result<Page<int>>))]
[JsonSerializable(typeof(Result<Dictionary<string, int>>))]
public partial class ResultTestJsonContext : JsonSerializerContext;

public sealed record ImportReceipt(int ImportedCount, string Message);
