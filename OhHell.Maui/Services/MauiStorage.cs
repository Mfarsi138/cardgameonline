using OhHell.Components.Services;

namespace OhHell.Maui.Services;

public sealed class MauiStorage : IPlatformStorage
{
    public Task<string?> GetItemAsync(string key)
    {
        var value = Preferences.Default.Get<string?>(key, null);
        return Task.FromResult(value);
    }

    public Task SetItemAsync(string key, string value)
    {
        Preferences.Default.Set(key, value);
        return Task.CompletedTask;
    }

    public Task RemoveItemAsync(string key)
    {
        Preferences.Default.Remove(key);
        return Task.CompletedTask;
    }

    public Task CopyToClipboardAsync(string text)
    {
        return Clipboard.Default.SetTextAsync(text);
    }

    public Task ShowToastAsync(string message)
    {
        // MAUI Toast is not available in all versions; use a simple no-op
        // The Blazor components handle toast display via their own UI
        return Task.CompletedTask;
    }
}
