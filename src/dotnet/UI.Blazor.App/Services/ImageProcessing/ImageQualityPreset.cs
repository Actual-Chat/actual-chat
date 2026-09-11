namespace ActualChat.UI.Blazor.App.Services;

public enum ImageQualityPreset
{
    Uhd4K = 0,
    FullHd,
    Original,
    OriginalWithExif,
}

public static class ImageQualityPresetExt
{
    public static int? GetMaxSize(this ImageQualityPreset preset)
        => preset switch {
            ImageQualityPreset.Uhd4K => 3840,
            ImageQualityPreset.FullHd => 1920,
            _ => null,
        };

    public static ImageProcessRequest ToRequest(this ImageQualityPreset preset)
        // Re-encoding presets also encode the other size from the same decode, so the menu can show it
        => preset switch {
            ImageQualityPreset.Uhd4K => new([ImageOutputSpec.Main(3840), ImageOutputSpec.Estimate(1920)]),
            ImageQualityPreset.FullHd => new([ImageOutputSpec.Main(1920), ImageOutputSpec.Estimate(3840)]),
            ImageQualityPreset.Original => new([ImageOutputSpec.Original(stripMetadata: true)]),
            _ => new([ImageOutputSpec.Original(stripMetadata: false)]),
        };
}
