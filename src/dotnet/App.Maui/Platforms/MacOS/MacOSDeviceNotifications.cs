using ActualChat.Maui.Services;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.Generators;
using ActualLab.IO;
using CoreGraphics;
using Foundation;
using ImageIO;
using Microsoft.Maui.Storage;
using UniformTypeIdentifiers;
using UserNotifications;

namespace ActualChat.App.Maui;

// macOS IDeviceNotifications: full create + prune. The AppKit backend has no push path, so
// "create" IS delivery here: NotificationReconciler pulls the active set over the app's own
// RPC and every newly-active notification becomes a local UNNotificationRequest - which also
// means notifications appear only while the app is running, by design.
// The chat icon rides along as an image attachment, which macOS renders as the banner's
// thumbnail. A communication notification (INSendMessageIntent, the iOS route to an avatar
// in place of the app icon) was tried first: the update succeeds, but macOS 15.1+ draws the
// app icon regardless - Messages and WhatsApp lost their sender pictures the same way.
public class MacOSDeviceNotifications(IServiceProvider services) : IDeviceNotifications
{
    // A banner that lands seconds late is worse than an iconless one, and these are 128px
    // avatars: a slow fetch means the network is gone anyway.
    private static readonly TimeSpan IconFetchTimeout = TimeSpan.FromSeconds(5);
    private static readonly FilePath CircleIconCacheDir
        = new FilePath(FileSystem.CacheDirectory) | "notification-icons";
    private static readonly ILogger Log = StaticLog.For<MacOSDeviceNotifications>();

    private readonly SemaphoreSlim _reconcileLock = new(1, 1);
    // Last observed content per active tag; a change means a message the user wasn't alerted
    // about yet, which re-posts the banner the way a push would - even one they dismissed.
    // Seeded silently on first observation, like the reconciler's own createTags baseline.
    private readonly Dictionary<string, (string Title, string Text)> _lastContentByTag = new();

    private static UNUserNotificationCenter NotificationCenter => UNUserNotificationCenter.Current;

    private IconUI IconUI => field ??= services.GetRequiredService<IconUI>();

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
        var delivered = await NotificationCenter.GetDeliveredNotificationsAsync().ConfigureAwait(false);

        var shownByTag = new Dictionary<string, UNNotification>();
        var toRemove = new List<string>();
        foreach (var notification in delivered) {
            var tag = notification.Request.Content.ThreadIdentifier;
            if (tag.IsNullOrEmpty())
                continue;
            if (!activeTags.Contains(tag))
                toRemove.Add(notification.Request.Identifier);
            else
                shownByTag[tag] = notification;
        }
        if (toRemove.Count > 0)
            NotificationCenter.RemoveDeliveredNotifications(toRemove.ToArray());
        Log.LogDebug(
            "Reconcile: {ActiveCount} active, {CreateCount} to create, {ShownCount} shown, {RemovedCount} removed",
            active.Count, createTags.Count, shownByTag.Count, toRemove.Count);

        foreach (var info in active) {
            var isChanged = _lastContentByTag.TryGetValue(info.Tag, out var last)
                && (last.Title != info.Title || last.Text != info.Text);
            var isNew = createTags.Contains(info.Tag) && !shownByTag.ContainsKey(info.Tag);
            _lastContentByTag[info.Tag] = (info.Title, info.Text);
            if (!isNew && !isChanged)
                continue;

            var hasIcon = await Post(info).ConfigureAwait(false);
            Log.LogDebug("Reconcile: {Action} notification for tag {Tag}, icon: {HasIcon}",
                isChanged ? "replaced" : "created", info.Tag, hasIcon);
        }

        foreach (var staleTag in _lastContentByTag.Keys.Where(t => !activeTags.Contains(t)).ToList())
            _lastContentByTag.Remove(staleTag);
    }

    private async Task<bool> Post(ActiveNotificationInfo info)
    {
        using var content = new UNMutableNotificationContent {
            Title = info.Title,
            Body = info.Text,
            ThreadIdentifier = info.Tag,
            // Unlike the other platforms, this is delivery rather than healing a dropped
            // push, so it alerts.
            Sound = UNNotificationSound.Default,
            UserInfo = NSDictionary.FromObjectAndKey(
                new NSString(info.Url),
                new NSString(Constants.Notification.MessageDataKeys.Link)),
        };
        using var icon = await GetIconAttachment(info).ConfigureAwait(false);
        if (icon is not null)
            content.Attachments = [icon];

        // The tag doubles as the identifier, so a re-posted tag replaces its predecessor
        var request = UNNotificationRequest.FromIdentifier(info.Tag, content, null);
        await NotificationCenter.AddNotificationRequestAsync(request).ConfigureAwait(false);
        return icon is not null;
    }

    private async Task<UNNotificationAttachment?> GetIconAttachment(ActiveNotificationInfo info)
    {
        // Null means "post the plain banner": no icon, or anything failing on the way to one.
        if (info.IconUrl.IsNullOrEmpty())
            return null;

        var iconPath = await FetchIcon(info.IconUrl).ConfigureAwait(false);
        if (iconPath.IsEmpty)
            return null;

        // The circle is rendered once per icon; every post hands over a fresh copy, since the
        // system moves the attached file into its own store.
        var attachmentPath = new FilePath(Path.GetTempPath()) & $"icon-{RandomStringGenerator.Default.Next()}.png";
        try {
            var circlePath = CircleIconCacheDir | (iconPath.FileNameWithoutExtension + ".png");
            if (!File.Exists(circlePath)) {
                Directory.CreateDirectory(CircleIconCacheDir);
                if (!WriteCircularPng(iconPath, circlePath)) {
                    Log.LogWarning("Icon isn't a decodable image: {IconUrl}", info.IconUrl);
                    return null;
                }
            }
            File.Copy(circlePath, attachmentPath);

            var options = new UNNotificationAttachmentOptions { TypeHint = UTTypes.Png.Identifier };
            var attachment = UNNotificationAttachment.FromIdentifier(
                "icon", NSUrl.CreateFileUrl(attachmentPath), options, out var error);
            if (attachment is not null && error is null)
                return attachment;

            Log.LogWarning("Icon attachment failed for {IconUrl}: {Error}",
                info.IconUrl, error?.LocalizedDescription);
            attachment?.Dispose();
        }
        catch (Exception e) {
            Log.LogWarning(e, "Icon attachment failed for {IconUrl}", info.IconUrl);
        }
        File.Delete(attachmentPath);
        return null;
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

    // The thumbnail slot is a rounded square, so the circle comes from the image itself:
    // the centered square of the source clipped to an ellipse over transparent corners.
    // TODO(#4442): drop this once the server serves round icons - attach the cached file as is.
    private static bool WriteCircularPng(FilePath sourcePath, FilePath targetPath)
    {
        using var source = CGImageSource.FromUrl(NSUrl.CreateFileUrl(sourcePath));
        using var image = source?.CreateImage(0, null);
        if (image is null)
            return false;

        var size = (int)Math.Min(image.Width, image.Height);
        using var colorSpace = CGColorSpace.CreateDeviceRGB();
        using var context = new CGBitmapContext(
            null, size, size, 8, size * 4, colorSpace, CGImageAlphaInfo.PremultipliedLast);
        context.AddEllipseInRect(new CGRect(0, 0, size, size));
        context.Clip();
        var origin = new CGPoint((size - (int)image.Width) / 2, (size - (int)image.Height) / 2);
        context.DrawImage(new CGRect(origin, new CGSize(image.Width, image.Height)), image);
        using var circle = context.ToImage();
        if (circle is null)
            return false;

        using var destination = CGImageDestination.Create(NSUrl.CreateFileUrl(targetPath), UTTypes.Png.Identifier, 1);
        if (destination is null)
            return false;

        destination.AddImage(circle, (CGImageDestinationOptions?)null);
        return destination.Close();
    }
}
