using System.Text.Json.Serialization;

namespace NOF.Contract.Tests;

[JsonSerializable(typeof(Result<int>))]
public partial class ResultTestJsonContext : JsonSerializerContext;
