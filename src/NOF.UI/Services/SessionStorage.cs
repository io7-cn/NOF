using Microsoft.JSInterop;

namespace NOF.UI;

// This adapter propagates JS failures (including failed writes) to the caller that owns recovery.
#pragma warning disable BL0016
public sealed class SessionStorage(IJSRuntime jsRuntime) : ISessionStorage
{
    public ValueTask<string?> GetItemAsync(string key)
        => jsRuntime.InvokeAsync<string?>("sessionStorage.getItem", key);

    public ValueTask SetItemAsync(string key, string value)
        => jsRuntime.InvokeVoidAsync("sessionStorage.setItem", key, value);

    public ValueTask RemoveItemAsync(string key)
        => jsRuntime.InvokeVoidAsync("sessionStorage.removeItem", key);
}
#pragma warning restore BL0016
