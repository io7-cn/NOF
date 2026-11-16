using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing.Patterns;
using NOF.Abstraction;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace NOF.Hosting.AspNetCore;

/// <summary>A declared request member source.</summary>
public sealed record HttpRequestMember(string Member, HttpInputLocation Source, string Name, Type ValueType, bool HasDefault = false, object? DefaultValue = null);
