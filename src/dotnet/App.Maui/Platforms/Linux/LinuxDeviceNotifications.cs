using ActualChat.Maui.Services;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using ActualLab.IO;

namespace ActualChat.App.Maui;

// Linux IDeviceNotifications: create + prune + replace, as in the AppKit app. There is no push
// path here either, so "create" IS delivery: NotificationReconciler pulls the active set over
// the app's own RPC and every newly-active notification becomes a desktop notification - which
// also means notifications appear only while the app is running.
// The desktop can't be asked what it currently shows, so the shown tags are tracked here.
public sealed class LinuxDeviceNotifications(IServiceProvider services) : IDeviceNotifications
{
    private const string OpenActionName = "open-notification";
    // A banner that lands seconds late is worse than an iconless one, and these are 128px
    // avatars: a slow fetch means the network is gone anyway.
    private static readonly TimeSpan IconFetchTimeout = TimeSpan.FromSeconds(5);
    private static readonly ILogger Log = StaticLog.For<LinuxDeviceNotifications>();

    private readonly SemaphoreSlim _reconcileLock = new(1, 1);
    private readonly HashSet<string> _shownTags = [];
    // Last observed content per active tag; a change means a message the user wasn't alerted
    // about yet, which re-posts the banner the way a push would - even one they dismissed.
    private readonly Dictionary<string, (string Title, string Text)> _lastContentByTag = new();

    private IconUI IconUI => field ??= services.GetRequiredService<IconUI>();

    // The action a click on a notification activates; the desktop reaches it over D-Bus.
    // UI thread only.
    public static void AddOpenAction()
    {
        var action = Gio.SimpleAction.New(OpenActionName, GLib.VariantType.String);
        action.OnActivate += static (_, args) => {
            var url = args.Parameter?.GetString(out var length);
            if (url.IsNullOrEmpty())
                return;

            AppNavigationQueue.EnqueueOrNavigateToUrl(url, AutoNavigationReason.Notification);
            WindowConfigurator.ShowWindow();
        };
        Gio.Application.GetDefault()!.AddAction(action);
    }

    public async Task Reconcile(
        IReadOnlyList<ActiveNotificationInfo> active,
        IReadOnlyCollection<string> createTags,
        CancellationToken cancellationToken)
    {
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
        var activeTags = active.Select(x => x.Tag).ToHashSet();
        var staleTags = _shownTags.Where(t => !activeTags.Contains(t)).ToList();
        // The in-app UI covers the case of a window the user is working in
        var isWindowActive = await DispatchToMainThread(() => WindowConfigurator.IsWindowActive).ConfigureAwait(false);
        var posts = new List<(ActiveNotificationInfo Info, FilePath IconPath)>();
        foreach (var info in active) {
            var isChanged = _lastContentByTag.TryGetValue(info.Tag, out var last)
                && (last.Title != info.Title || last.Text != info.Text);
            var isNew = createTags.Contains(info.Tag) && !_shownTags.Contains(info.Tag);
            _lastContentByTag[info.Tag] = (info.Title, info.Text);
            if (isWindowActive || !(isNew || isChanged))
                continue;

            var iconPath = await FetchIcon(info.IconUrl).ConfigureAwait(false);
            posts.Add((info, iconPath));
        }
        foreach (var staleTag in _lastContentByTag.Keys.Where(t => !activeTags.Contains(t)).ToList())
            _lastContentByTag.Remove(staleTag);
        Log.LogDebug(
            "Reconcile: {ActiveCount} active, {CreateCount} to create, {PostCount} posted, {RemovedCount} removed",
            active.Count, createTags.Count, posts.Count, staleTags.Count);
        if (posts.Count == 0 && staleTags.Count == 0)
            return;

        await DispatchToMainThread(() => {
            var application = Gio.Application.GetDefault()!;
            foreach (var staleTag in staleTags) {
                application.WithdrawNotification(staleTag);
                _shownTags.Remove(staleTag);
            }
            foreach (var (info, iconPath) in posts) {
                // The tag doubles as the id, so a re-posted tag replaces its predecessor
                application.SendNotification(info.Tag, NewNotification(info, iconPath));
                _shownTags.Add(info.Tag);
            }
        }).ConfigureAwait(false);
    }

    private async Task<FilePath> FetchIcon(string iconUrl)
    {
        using var timeoutCts = new CancellationTokenSource(IconFetchTimeout);
        try {
            return await IconUI.FetchImage(iconUrl, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) {
            Log.LogWarning("Icon fetch timed out: {IconUrl}", iconUrl);
            return FilePath.Empty;
        }
    }

    private static Gio.Notification NewNotification(ActiveNotificationInfo info, FilePath iconPath)
    {
        var notification = Gio.Notification.New(info.Title);
        notification.SetBody(info.Text);
        if (!iconPath.IsEmpty)
            notification.SetIcon(Gio.FileIcon.New(Gio.FileHelper.NewForPath(iconPath)));
        // "action::target" is GAction's detailed name for an action with a string parameter
        notification.SetDefaultAction($"app.{OpenActionName}::{info.Url}");
        return notification;
    }
}
