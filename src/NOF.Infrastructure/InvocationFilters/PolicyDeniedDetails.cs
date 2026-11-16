using Microsoft.Extensions.Options;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using NOF.Hosting;
using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;

namespace NOF.Infrastructure;
/// <summary>Custom authorization provider denial data.</summary>
public sealed record PolicyDeniedDetails(string Reason);
