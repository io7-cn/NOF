using NOF.Abstraction;
using NOF.Application;
using NOF.Infrastructure;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace NOF.Sample.Services;

public sealed record DemoEvent(string Text, DateTimeOffset PublishedAt);
