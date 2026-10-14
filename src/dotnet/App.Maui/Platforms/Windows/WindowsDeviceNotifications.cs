using ActualChat.Hashing;
using ActualChat.Localization;
using ActualChat.Maui.Services;
using ActualChat.Notifications;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.IO;
using Microsoft.Extensions.Localization;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace ActualChat.App.Maui;

// Windows IDeviceNotifications: full create + prune. The Windows app has no push path, so
// "create" IS delivery here, exactly as in MacOSDeviceNotifications: NotificationReconciler
// pulls the active set over the app's own RPC and every newly-active notification becomes a
// toast - which also means toasts appear only while the app is running. Closing the window
// only minimizes it, so that is the usual state.
public class WindowsDeviceNotifications(IServiceProvider services) : IDeviceNotifications
{
    // Windows rejects a toast with a longer tag
    private const int MaxToastTagLength = 64;
    private const int MaxToastTextLines = 4;
    // A toast that lands seconds late is worse than an iconless one, and these are 128px
    // avatars: a slow fetch means the network is gone anyway.
    private static readonly TimeSpan IconFetchTimeout = TimeSpan.FromSeconds(5);
    private static readonly ILogger Log = StaticLog.For<WindowsDeviceNotifications>();

    private readonly SemaphoreSlim _reconcileLock = new(1, 1);
    // Last observed content per active tag; a change means a message the user wasn't alerted
    // about yet, which re-posts the toast the way a push would - even one they dismissed.
    // Seeded silently on first observation, like the reconciler's own createTags baseline.
    private readonly Dictionary<string, (string Title, string Text)> _lastContentByTag = new();

    private IconUI IconUI => field ??= services.GetRequiredService<IconUI>();
    private IStringLocalizer L => field ??= services.GetRequiredService<IStringLocalizer>();

    public async Task Reconcile(
        IReadOnlyList<ActiveNotificationInfo> active,
        IReadOnlyCollection<string> createTags,
        CancellationToken cancellationToken)
    {
        if (!WindowsNotificationActivation.IsRegistered)
            return;

        await _reconcileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            await ReconcileUnsafe(active, createTags).ConfigureAwait(false);
        }
        finally {
            _reconcileLock.Release();
        }
    }

    // Private methods

    private async Task ReconcileUnsafe(
        IReadOnlyList<ActiveNotificationInfo> active,
        IReadOnlyCollection<string> createTags)
    {
        var manager = AppNotificationManager.Default;
        var activeTags = active.Select(x => x.Tag).ToHashSet();
        var activeToastTags = activeTags.Select(GetToastTag).ToHashSet();
        var shown = await manager.GetAllAsync().AsTask().ConfigureAwait(false);

        var shownToastTags = new HashSet<string>();
        var removedCount = 0;
        foreach (var notification in shown) {
            var toastTag = notification.Tag;
            if (toastTag.IsNullOrEmpty())
                continue;

            if (activeToastTags.Contains(toastTag))
                shownToastTags.Add(toastTag);
            else {
                await manager.RemoveByIdAsync(notification.Id).AsTask().ConfigureAwait(false);
                removedCount++;
            }
        }
        Log.LogDebug(
            "Reconcile: {ActiveCount} active, {CreateCount} to create, {ShownCount} shown, {RemovedCount} removed",
            active.Count, createTags.Count, shownToastTags.Count, removedCount);

        // The in-app UI covers a window the user is looking at
        var mustPost = !WindowConfigurator.IsInForeground;
        foreach (var info in active) {
            var isChanged = _lastContentByTag.TryGetValue(info.Tag, out var last)
                && (last.Title != info.Title || last.Text != info.Text);
            var isNew = createTags.Contains(info.Tag) && !shownToastTags.Contains(GetToastTag(info.Tag));
            _lastContentByTag[info.Tag] = (info.Title, info.Text);
            if (!mustPost || (!isNew && !isChanged))
                continue;

            var hasIcon = await Post(info).ConfigureAwait(false);
            Log.LogDebug("Reconcile: {Action} toast for tag {Tag}, icon: {HasIcon}",
                isChanged ? "replaced" : "created", info.Tag, hasIcon);
        }

        foreach (var staleTag in _lastContentByTag.Keys.Where(t => !activeTags.Contains(t)).ToList())
            _lastContentByTag.Remove(staleTag);
    }

    private async Task<bool> Post(ActiveNotificationInfo info)
    {
        var tagKey = Constants.Notification.MessageDataKeys.Tag;
        var builder = new AppNotificationBuilder()
            .AddArgument(tagKey, info.Tag)
            .AddArgument(Constants.Notification.MessageDataKeys.Link, info.Url)
            .AddText(info.Title)
            .AddText(GetToastText(info.Text))
            // The tag makes a re-posted toast replace its predecessor
            .SetTag(GetToastTag(info.Tag));
        if (NotificationExt.IsCallTag(info.Tag)) {
            var accept = new AppNotificationButton(L.Common_Accept)
                .AddArgument(tagKey, info.Tag)
                .AddArgument(WindowsNotificationActivation.ActionKey, WindowsNotificationActivation.AcceptCallAction);
            var decline = new AppNotificationButton(L.Common_Decline)
                .AddArgument(tagKey, info.Tag)
                .AddArgument(WindowsNotificationActivation.ActionKey, WindowsNotificationActivation.DeclineCallAction);
            // The scenario keeps the toast on screen while the call rings, and it's muted
            // since the app's own ringtone already sounds.
            builder = builder
                .SetScenario(AppNotificationScenario.IncomingCall)
                .MuteAudio()
                .AddButton(accept)
                .AddButton(decline);
        }
        var iconPath = await FetchIcon(info.IconUrl).ConfigureAwait(false);
        // The cache folder is named after the company, "Actual Chat Inc.", and its trailing dot
        // exists only in the string: file APIs drop it, the shell's image loader doesn't.
        if (!iconPath.IsEmpty)
            builder = builder.SetAppLogoOverride(new Uri(Path.GetFullPath(iconPath)), AppNotificationImageCrop.Circle);

        AppNotificationManager.Default.Show(builder.BuildNotification());
        return !iconPath.IsEmpty;
    }

    private async Task<FilePath> FetchIcon(string iconUrl)
    {
        // Empty means "post the plain toast": no icon, or anything failing on the way to one
        if (iconUrl.IsNullOrEmpty())
            return FilePath.Empty;

        using var timeoutCts = new CancellationTokenSource(IconFetchTimeout);
        try {
            return await IconUI.FetchImage(iconUrl, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Icon fetch failed: {IconUrl}", iconUrl);
            return FilePath.Empty;
        }
    }

    private static string GetToastText(string text)
    {
        // A toast draws the first lines of its body and drops the rest, while a coalesced
        // notification lists its messages oldest first - so the newest ones are what's kept.
        var lines = text.Split('\n');
        return lines.Length <= MaxToastTextLines
            ? text
            : string.Join('\n', lines[^MaxToastTextLines..]);
    }

    private static string GetToastTag(string tag)
        => tag.Length <= MaxToastTagLength
            ? tag
            : tag.Hash().SHA256().AlphaNumeric();
}
