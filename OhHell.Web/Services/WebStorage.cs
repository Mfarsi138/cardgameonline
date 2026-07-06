using Microsoft.JSInterop;
using OhHell.Components.Services;

namespace OhHell.Web.Services;

public sealed class WebStorage(IJSRuntime js) : IPlatformStorage
{
    public async Task<string?> GetItemAsync(string key)
    {
        try
        {
            return await js.InvokeAsync<string?>("localStorage.getItem", key);
        }
        catch
        {
            return null;
        }
    }

    public async Task SetItemAsync(string key, string value)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", key, value);
        }
        catch
        {
        }
    }

    public async Task RemoveItemAsync(string key)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.removeItem", key);
        }
        catch
        {
        }
    }

    public async Task CopyToClipboardAsync(string text)
    {
        try
        {
            await js.InvokeVoidAsync("navigator.clipboard.writeText", text);
        }
        catch
        {
        }
    }

    public async Task ShowToastAsync(string message)
    {
        try
        {
            await js.InvokeVoidAsync("ohHellToast", message);
        }
        catch
        {
        }
    }
}
