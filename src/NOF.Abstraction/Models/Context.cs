using System.Text.Json;
using System.Text.Json.Nodes;

namespace NOF.Abstraction;

/// <summary>
/// Immutable execution context passed explicitly across NOF execution boundaries.
/// </summary>
/// <remarks>
/// Items have case-insensitive string keys and JSON scalar values. Snapshots never expose mutable owned nodes.
/// </remarks>
public class Context
{
    private static readonly AsyncLocal<Context?> AmbientContext = new();
    private readonly JsonObject _items;

    protected Context()
        : this(new JsonObject())
    {
    }

    protected Context(JsonObject items)
    {
        _items = Snapshot(items);
    }

    public static Context Empty { get; } = new();

    /// <summary>
    /// Gets the context bound to the current asynchronous control flow.
    /// </summary>
    public static Context Current => AmbientContext.Value ?? Empty;

    /// <summary>Gets a detached JSON object snapshot containing only JSON scalar values.</summary>
    public JsonObject Items => (JsonObject)_items.DeepClone();

    /// <summary>
    /// Gets the normalized tenant identifier carried by this context.
    /// </summary>
    public string TenantId
        => _items.TryGetPropertyValue(NOFAbstractionConstants.Transport.Headers.TenantId, out var value)
            && value is JsonValue json && json.TryGetValue<string>(out var tenantId)
                ? NOFAbstractionConstants.Tenant.NormalizeTenantId(tenantId)
                : NOFAbstractionConstants.Tenant.HostId;

    public JsonNode? this[string key]
        => TryGetItem(key, out var value)
            ? value
            : null;

    public bool TryGetItem(string key, out JsonNode? value)
    {
        ArgumentNullException.ThrowIfNull(key);
        var found = _items.TryGetPropertyValue(key, out var stored);
        value = stored?.DeepClone();
        return found;
    }

    public Context WithItem(string key, JsonNode? value)
    {
        ArgumentNullException.ThrowIfNull(key);

        var items = Items;
        items[key] = value?.DeepClone();
        return Clone(items);
    }

    /// <summary>
    /// Creates a context carrying the specified normalized tenant identifier.
    /// </summary>
    public Context WithTenantId(string? tenantId)
        => WithItem(
            NOFAbstractionConstants.Transport.Headers.TenantId,
            NOFAbstractionConstants.Tenant.NormalizeTenantId(tenantId));

    /// <summary>
    /// Binds a context to the current asynchronous control flow until the returned scope is disposed.
    /// </summary>
    public static IDisposable PushCurrent(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (ReferenceEquals(AmbientContext.Value, context))
        {
            return EmptyScope.Instance;
        }

        var previous = AmbientContext.Value;
        AmbientContext.Value = context;
        return new AmbientContextScope(previous);
    }

    public Context WithItems(JsonObject items)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            return this;
        }

        var merged = Items;
        foreach (var item in items)
        {
            merged[item.Key] = item.Value?.DeepClone();
        }

        return Clone(merged);
    }

    public Context WithoutItem(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!_items.ContainsKey(key))
        {
            return this;
        }

        var items = Items;
        items.Remove(key);
        return Clone(items);
    }

    protected virtual Context Clone(JsonObject items)
        => new(items);

    private static JsonObject Snapshot(JsonObject items)
    {
        var result = new JsonObject(new JsonNodeOptions { PropertyNameCaseInsensitive = true });
        foreach (var item in items)
        {
            if (item.Value is not null && (item.Value is not JsonValue
                || item.Value.GetValueKind() is JsonValueKind.Object or JsonValueKind.Array))
            {
                throw new ArgumentException("Context items must be JSON strings, numbers, booleans or null.", nameof(items));
            }
            result.Add(item.Key, item.Value is null ? null : JsonNode.Parse(item.Value.ToJsonString()));
        }

        return result;
    }

    private sealed class AmbientContextScope(Context? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            AmbientContext.Value = previous;
            _disposed = true;
        }
    }

    private sealed class EmptyScope : IDisposable
    {
        public static EmptyScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
