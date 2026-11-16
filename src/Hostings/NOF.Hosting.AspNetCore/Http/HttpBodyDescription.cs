using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using System.Diagnostics.CodeAnalysis;

namespace NOF.Hosting.AspNetCore;

/// <summary>Describes an externally supplied opaque input or writer without probing delegates.</summary>
public sealed record HttpBodyDescription(Type BodyType, string ContentType = "application/json", bool Required = true);
