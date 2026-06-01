using ActualChat.Chat;
using ActualChat.Kvas;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Tracks the user's recently picked mentions (users, chats, places, emoji) in account settings,
/// so they're synced across devices and can boost the mention picker's ranking.
/// </summary>
public sealed class RecentMentionsUI : UIServiceBase<AppUIHub>, IDisposable
{
    public SyncedState<RecentMentions> Settings { get; }
    public Task WhenReady => Settings.WhenFirstTimeRead;

    public RecentMentionsUI(AppUIHub hub) : base(hub)
        => Settings = StateFactory.NewUserSettingsSynced(
            UserSettingsUI,
            RecentMentions.KvasKey,
            new RecentMentions(),
            updateDelayer: FixedDelayer.NextTick,
            category: StateCategories.Get(GetType(), nameof(Settings)));

    public void Dispose()
        => Settings.Dispose();

    public IReadOnlyDictionary<MentionRef, double> GetScores()
        => Settings.Value.GetScores(Clocks.SystemClock.Now);

    public async Task Use(MentionRef id, CancellationToken cancellationToken = default)
    {
        await WhenReady.ConfigureAwait(false);
        Settings.Set(Settings.Value.Use(id, Clocks.SystemClock.Now));
    }
}
