using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using NOF.Abstraction;
using NOF.Application;
using NOF.Domain;
using NOF.Infrastructure;
using NOF.Infrastructure.RabbitMQ;
using NOF.Sample.Services;
using System.Text;

namespace NOF.Sample.Endpoints;

public sealed record DemoInput(string Value = "NOF demo", string Tenant = "host", Guid? OrderId = null, string Email = "demo@example.test");
