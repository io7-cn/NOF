using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using NOF.Infrastructure;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace NOF.Hosting.AspNetCore;

/// <summary>One resolved CLR/JSON member and external source.</summary>
public sealed record HttpBoundMember(string ClrName, string JsonName, Type ValueType, HttpInputLocation Source, string SourceName, bool Required, bool AllowsNull, bool HasDefault, object? DefaultValue);
