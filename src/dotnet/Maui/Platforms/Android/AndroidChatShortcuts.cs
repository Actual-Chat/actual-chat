using System.Collections.Concurrent;
using Android.Content;
using Android.Graphics;
using AndroidX.Core.Content.PM;
using AndroidX.Core.Graphics.Drawable;
using Person = AndroidX.Core.App.Person;

namespace ActualChat.Maui;

// A chat reaches Android through one long-lived shortcut, keyed by the chat id: the share sheet
// lists it, and a notification that names it becomes a conversation notification - which is what
// makes the system draw the avatar itself, masked, instead of pasting our bitmap in a box.
// Both publishers build it here so the two can't drift apart.
public static class AndroidChatShortcuts
{
    public const string ShareTargetCategory = "chat.actual.app.category.SHARE_TARGET";

    private const int AdaptiveIconSafeZone = 108;
    private const int AdaptiveIconSize = AdaptiveIconSafeZone * 3 / 2;
    private const int AdaptiveIconInset = (AdaptiveIconSize - AdaptiveIconSafeZone) / 2;
    private const int MaxShortLabelLength = 25;
    private static readonly ConcurrentDictionary<string, byte> IconizedChatSids = new();

    public static void PushOnce(
        Context context,
        ChatId chatId,
        string title,
        string url,
        IconCompat? icon)
    {
        // Republishing on every notification would spend the shortcut rate limit the share sheet
        // draws on too, and the share path already refreshes title and icon whenever the user posts
        // to the chat. An iconless push isn't recorded, so the next one carrying one still lands.
        var chatSid = chatId.Value;
        if (IconizedChatSids.ContainsKey(chatSid))
            return;

        Push(context, chatId, title, url, icon);
        if (icon is not null)
            IconizedChatSids.TryAdd(chatSid, 0);
    }

    public static void Push(
        Context context,
        ChatId chatId,
        string title,
        string url,
        IconCompat? icon)
    {
        var chatSid = chatId.Value;
        var intent = new Intent(Intent.ActionDefault);
        intent.SetData(Android.Net.Uri.Parse(url));
        var shortLabel = title.Length > MaxShortLabelLength ? title[..MaxShortLabelLength] : title;

        var personBuilder = new Person.Builder()
            .SetName(title)!
            .SetKey(chatSid)!;
        if (icon is not null)
            personBuilder.SetIcon(icon);

        var builder = new ShortcutInfoCompat.Builder(context, chatSid)
            .SetShortLabel(shortLabel)!
            .SetLongLabel(title)!
            .SetIntent(intent)!
            .SetLongLived(true)!
            .SetCategories([ShareTargetCategory])!
            .SetPerson(personBuilder.Build())!;
        if (icon is not null)
            builder.SetIcon(icon);

        ShortcutManagerCompat.PushDynamicShortcut(context, builder.Build());
    }

    public static IconCompat CreateIcon(Bitmap avatar)
        => IconCompat.CreateWithAdaptiveBitmap(ToAdaptiveIcon(avatar))!;

    // Private methods

    private static Bitmap ToAdaptiveIcon(Bitmap source)
    {
        // CreateWithAdaptiveBitmap treats the whole bitmap as the icon canvas, but the
        // launcher mask reveals only the inner safe zone - the avatar is drawn into that
        // zone (center-cropped to a square) and the surrounding inset is left transparent.
        var result = Bitmap.CreateBitmap(AdaptiveIconSize, AdaptiveIconSize, Bitmap.Config.Argb8888!)!;
        using var canvas = new Canvas(result);
        using var paint = new Paint { AntiAlias = true, FilterBitmap = true };

        var side = Math.Min(source.Width, source.Height);
        var src = new Rect(
            (source.Width - side) / 2,
            (source.Height - side) / 2,
            (source.Width + side) / 2,
            (source.Height + side) / 2);
        var dest = new Rect(
            AdaptiveIconInset,
            AdaptiveIconInset,
            AdaptiveIconSize - AdaptiveIconInset,
            AdaptiveIconSize - AdaptiveIconInset);
        canvas.DrawBitmap(source, src, dest, paint);
        return result;
    }
}
