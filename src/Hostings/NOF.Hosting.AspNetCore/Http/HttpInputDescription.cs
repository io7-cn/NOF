using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using System.Diagnostics.CodeAnalysis;

namespace NOF.Hosting.AspNetCore;

/// <summary>One immutable, describable HTTP input.</summary>
public sealed record HttpInputDescription(string Name, HttpInputLocation Location, Type ValueType, bool Required, object? DefaultValue = null, string? ContentType = null);
