using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using System.Diagnostics.CodeAnalysis;

namespace NOF.Hosting.AspNetCore;

/// <summary>One actual response shape, after inheritance and projection.</summary>
public sealed record HttpResponseDescription(Type SourceType, int StatusCode, Type? BodyType, string? ContentType, string? Description = null);
