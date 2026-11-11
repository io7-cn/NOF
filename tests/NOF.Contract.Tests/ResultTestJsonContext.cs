using System.Text.Json.Serialization;

namespace NOF.Contract.Tests;

[JsonSerializable(typeof(Result<int>))]
[JsonSerializable(typeof(Result<Success<int>>))]
[JsonSerializable(typeof(Result<Page<int>>))]
[JsonSerializable(typeof(Result<Dictionary<string, int>>))]
public partial class ResultTestJsonContext : JsonSerializerContext;
