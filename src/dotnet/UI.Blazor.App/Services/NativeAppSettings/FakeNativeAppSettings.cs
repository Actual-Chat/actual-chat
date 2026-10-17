namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Lets <see cref="ActualChat.Hosting.HostInfo.MustShowAllUI"/> show native app settings on hosts that have none.
/// The auto-start flag is held in memory only.
/// </summary>
public sealed class FakeNativeAppSettings : INativeAppSettings
{
    private bool _isAutoStartEnabled;

    public Task<AutoStartState> GetAutoStartState()
        => Task.FromResult(new AutoStartState(_isAutoStartEnabled));

    public Task SetAutoStart(bool isEnabled)
    {
        _isAutoStartEnabled = isEnabled;
        return Task.CompletedTask;
    }
}
