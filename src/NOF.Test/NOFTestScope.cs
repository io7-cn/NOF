using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using NOF.Infrastructure;
using System.Diagnostics;
using System.Security.Claims;

namespace NOF.Test;

public sealed class NOFTestScope : IAsyncDisposable, IDisposable
{
    private readonly AsyncServiceScope _scope;
    private Context _context = Context.Empty;
    private IDisposable? _contextScope;
    private readonly IDisposable _ambientBinding;
    private Activity? _tracingActivity;

    public NOFTestScope(AsyncServiceScope scope)
    {
        _scope = scope;
        _ambientBinding = scope.ServiceProvider.BindNOF(_context);
    }

    public IServiceProvider Services => _scope.ServiceProvider;

    public T GetRequiredService<T>() where T : notnull
    {
        return Services.GetRequiredService<T>();
    }

    public Context Context => _context;

    public IUserContext UserContext => GetRequiredService<IUserContext>();

    public NOFTestScope SetTenant(string? tenantId)
    {
        return SetContext(Context.WithTenantId(TenantId.Normalize(tenantId)));
    }

    public NOFTestScope SetTracing(string? traceId, string? spanId)
    {
        if (traceId is not null && spanId is not null)
        {
            _tracingActivity?.Dispose();
            _tracingActivity = CreateTracingActivity(traceId, spanId);
        }
        return this;
    }

    public NOFTestScope SetUser(ClaimsPrincipal user)
    {
        UserContext.Logout();
        UserContext.User.AddIdentities(user.Identities.OfType<ClaimsIdentity>());
        return this;
    }

    public NOFTestScope SetAnonymousUser()
    {
        UserContext.Logout();
        return this;
    }

    public NOFTestScope SetContext(Context context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _contextScope?.Dispose();
        _contextScope = Context.PushCurrent(_context);
        return this;
    }

    public NOFTestScope SetContextItem(object key, object? value)
    {
        return SetContext(Context.WithItem(key, value));
    }

    public NOFTestScope SetContextItems(IReadOnlyDictionary<object, object?> items)
    {
        return SetContext(Context.WithItems(items));
    }

    public NOFTestScope RemoveContextItem(object key)
    {
        return SetContext(Context.WithoutItem(key));
    }

    public NOFTestScope SetUser(string userId, string? name = null, IEnumerable<string>? permissions = null, string authenticationType = "Test")
    {
        var claims = new List<Claim>
        {
            new("sub", userId)
        };

        if (!string.IsNullOrWhiteSpace(name))
        {
            claims.Add(new Claim("name", name));
        }

        if (permissions is not null)
        {
            foreach (var permission in permissions)
            {
                claims.Add(new Claim(ClaimTypes.Permission, permission));
            }
        }

        return SetUser(new ClaimsPrincipal(new ClaimsIdentity(
            claims,
            authenticationType,
            nameType: "name",
            roleType: ClaimTypes.Role)));
    }

    /// <summary>Executes a server operation using this scope's service provider and explicit context.</summary>
    public Task<TResult> CallAsync<TResult>(
        Func<IServiceProvider, Context, CancellationToken, Task<TResult>> invocation,
        Context? context = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        return invocation(_scope.ServiceProvider, context ?? Context, cancellationToken);
    }

    /// <summary>Stages an intent in this scope; save its unit of work explicitly before disposing.</summary>
    public ValueTask<Guid> EnqueueAsync<T>(T message, Context? context = null,
        IReadOnlyDictionary<string, string?>? headers = null, CancellationToken cancellationToken = default)
        => GetRequiredService<IOutbox>().EnqueueAsync(message, context ?? Context, headers, cancellationToken);

    public void Dispose()
    {
        _contextScope?.Dispose();
        _ambientBinding.Dispose();
        _tracingActivity?.Dispose();
        _scope.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        _contextScope?.Dispose();
        _ambientBinding.Dispose();
        _tracingActivity?.Dispose();
        return _scope.DisposeAsync();
    }

    private static Activity CreateTracingActivity(string traceId, string spanId)
    {
        var activity = new Activity("NOF.Test.Scope");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.SetParentId($"00-{traceId}-{spanId}-01");
        activity.Start();
        return activity;
    }
}
