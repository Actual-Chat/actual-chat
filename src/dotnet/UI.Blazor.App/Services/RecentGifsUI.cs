using ActualChat.Chat;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Tracks the user's recently used GIFs in account settings (synced across devices) as a
/// most-recently-used list. Replaces the old <c>GifPicker.Recent</c> local-storage entry.
/// </summary>
public sealed class RecentGifsUI : UIServiceBase<AppUIHub>, IDisposable
{
    public SyncedState<RecentGifs> Settings { get; }
    public Task WhenReady => Settings.WhenFirstTimeRead;

    public RecentGifsUI(AppUIHub hub) : base(hub)
        => Settings = StateFactory.NewUserSettingsSynced(
            UserSettingsUI,
            RecentGifs.KvasKey,
            new RecentGifs(),
            updateDelayer: FixedDelayer.NextTick,
            category: StateCategories.Get(GetType(), nameof(Settings)));

    public void Dispose()
        => Settings.Dispose();

    public async Task Use(RecentGif gif, CancellationToken cancellationToken = default)
    {
        await WhenReady.ConfigureAwait(false);
        Settings.Set(Settings.Value.Use(gif));
    }
}
