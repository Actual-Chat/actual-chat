using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace ActualChat.Mui;

// The comparison settings are shared by all dashboards of the current user and kept in the browser
public sealed class MuiPeriodState(ProtectedLocalStorage storage)
{
    private const string StorageKey = "mui-periods";

    private bool _isLoaded;

    public MuiPeriodSettings Settings { get; private set; } = new();

    public event Action? Changed;

    public async Task EnsureLoaded()
    {
        if (_isLoaded)
            return;

        _isLoaded = true;
        try {
            var result = await storage.GetAsync<string>(StorageKey);
            if (result.Success && result.Value is { } text) {
                Settings = MuiPeriodSettings.FromStorageString(text);
                Changed?.Invoke();
            }
        }
        catch {
            // Browser storage can be unavailable or hold data protected with another key: defaults are fine
        }
    }

    public async Task Update(MuiPeriodSettings settings)
    {
        if (settings == Settings)
            return;

        Settings = settings;
        Changed?.Invoke();
        try {
            await storage.SetAsync(StorageKey, settings.ToStorageString());
        }
        catch {
            // Not being able to remember the choice is not an error
        }
    }
}
