namespace OhHell.Components.Services;

public interface IPlatformStorage
{
    Task<string?> GetItemAsync(string key);
    Task SetItemAsync(string key, string value);
    Task RemoveItemAsync(string key);
    Task CopyToClipboardAsync(string text);
    Task ShowToastAsync(string message);
}
