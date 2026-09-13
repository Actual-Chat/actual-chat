namespace ActualChat.UI.Blazor.App.Services;

public static class ImageQualityMenuModel
{
    public static bool ExceedsServerBounds(Size2D size)
        => size.Width > Constants.Attachments.MaxImageSize
            || size.Height > Constants.Attachments.MaxImageSize
            || (long)size.Width * size.Height > Constants.Attachments.MaxImagePixelCount;

    public static bool IsSentAsFile(IReadOnlyList<Attachment> images)
        // The server stores an oversize source as a file no matter which Original variant asked
        // for it, and the same passthrough happens when a resize preset is declined on-device
        => images.Any(a => ExceedsServerBounds(a.Source!.Size));

    public static bool IsDeclinedAt(IReadOnlyList<Attachment> images, ImageQualityPreset preset)
        => images.Any(a => a.SelectedQuality == preset && a.IsDeclined);

    public static PresetTotal? GetTotal(IReadOnlyList<Attachment> images, long otherFilesLength, ImageQualityPreset preset)
    {
        var budget = preset.GetBudget();
        var isPassthrough = budget.MaxPixels is null && budget.MaxLongSide is null;
        var total = otherFilesLength;
        var isExact = true;
        foreach (var image in images) {
            var source = image.Source!;
            if (isPassthrough) {
                // Original/Original with EXIF never re-encode, so the source's own bytes are exact
                total += source.Length;
                continue;
            }
            if (image.SelectedQuality == preset && !image.IsProcessing) {
                // Already resolved at this exact preset: Length is the real upload - the output when
                // the encode succeeded, or the source when it was declined or silently kept as-is
                total += image.Length;
                continue;
            }
            if (source.Size.Width <= 0 || source.Size.Height <= 0)
                // Unknown source dimensions (e.g. an unconverted HEIC on Chromium) make the whole
                // row's total meaningless, not just this image's share of it
                return null;

            total += ImageSizeEstimator.Estimate(source.Length, source.Size, source.FileType, budget);
            isExact = false;
        }
        return new PresetTotal(total, isExact);
    }

    public readonly record struct PresetTotal(long Bytes, bool IsExact);
}
