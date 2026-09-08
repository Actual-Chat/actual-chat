using UniformTypeIdentifiers;

namespace ActualChat.Maui;

public static class NSItemProviderExt
{
    private static readonly string[] PreferredMainUTTypeIds = [
        "public.jpeg",
        "public.png",
        "public.heic",
        "public.heif",
        "public.tiff",
        "public.webp",
        "public.gif",
        "public.mpeg-4",
        "public.movie",
        "com.apple.quicktime-movie",
    ];

    public static UTType PickMainContentType(this NSItemProvider itemProvider)
    {
        var registered = itemProvider.RegisteredContentTypes;

        // Try preferred concrete UTIs in priority order.
        foreach (var preferred in PreferredMainUTTypeIds) {
            var match = registered.FirstOrDefault(t => string.Equals(t.Identifier, preferred, StringComparison.Ordinal));
            if (match is not null)
                return match;
        }

        // Fall back to first acceptable type — skip Live Photo bundles and Apple-private types
        // (e.g. `com.apple.private.photos.thumbnail.*`).
        var fallback = registered.FirstOrDefault(IsAcceptableMainType);
        if (fallback is not null)
            return fallback;

        throw StandardError.NotSupported<UTType>(
            $"No suitable main content type in: {string.Join(", ", registered.Select(t => t.Identifier))}");
    }

    private static bool IsAcceptableMainType(UTType type)
        => !string.Equals(type.Identifier, "com.apple.live-photo-bundle", StringComparison.Ordinal)
            && !type.Identifier.StartsWith("com.apple.private.", StringComparison.Ordinal);
}
