using ActualLab.Generators;

namespace ActualChat.App.Maui;

/// <summary>
/// Maps opaque keys to local content references — file paths or <c>content://</c> URIs —
/// that the app serves into its WebView. Keys are issued here, so the references reachable
/// from the WebView are exactly the ones the app published during this process run.
/// </summary>
public static class LocalContentRegistry
{
    private static readonly Lock Lock = new();
    private static readonly Dictionary<string, string> KeyByContentRef = new();
    private static readonly Dictionary<string, string> ContentRefByKey = new();

    public static string GetOrAddKey(string contentRef)
    {
        lock (Lock) {
            if (KeyByContentRef.TryGetValue(contentRef, out var key))
                return key;

            key = RandomStringGenerator.Default.Next(24);
            KeyByContentRef.Add(contentRef, key);
            ContentRefByKey.Add(key, contentRef);
            return key;
        }
    }

    public static bool TryGetContentRef(string key, [NotNullWhen(true)] out string? contentRef)
    {
        lock (Lock)
            return ContentRefByKey.TryGetValue(key, out contentRef);
    }
}
