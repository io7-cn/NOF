using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NOF.Abstraction;
using System.Reflection;

namespace NOF.Hosting;

/// <summary>Records an assembly whose generated registrations have been applied to this service collection.</summary>
public sealed record ApplicationPartRegistration(Assembly Assembly);
