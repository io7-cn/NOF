using NOF.Abstraction;
using NOF.Application;
using NOF.Infrastructure;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace NOF.Sample.Services;

public sealed record DemoInvocation(string Operation, string Origin, string Tenant, double Milliseconds, string Outcome, string? TraceId);
