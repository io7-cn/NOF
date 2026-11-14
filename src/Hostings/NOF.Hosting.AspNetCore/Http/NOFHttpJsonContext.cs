using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;

namespace NOF.Hosting.AspNetCore;

/// <summary>Framework HTTP payload metadata for reflection-free hosts.</summary>
[JsonSerializable(typeof(ProblemDetails))]
public partial class NOFHttpJsonContext : JsonSerializerContext;
