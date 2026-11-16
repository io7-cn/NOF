using Microsoft.Extensions.Options;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using NOF.Hosting;
using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;

namespace NOF.Infrastructure;

/// <summary>Authorization failure data, independent of HTTP status and header presentation.</summary>
public sealed record PermissionDeniedDetails(IReadOnlyList<string> RequiredPermissions);
