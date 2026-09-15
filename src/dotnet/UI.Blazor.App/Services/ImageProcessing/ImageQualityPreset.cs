namespace ActualChat.UI.Blazor.App.Services;

public enum ImageQualityPreset
{
    Mpx12 = 0,
    Mpx50,
    Mpx3,
    Original,
    OriginalWithExif,
}

public readonly record struct ImageQualityBudget(int? MaxPixels, int? MaxLongSide);

public static class ImageQualityPresetExt
{
    public static ImageQualityBudget GetBudget(this ImageQualityPreset preset)
        => preset switch {
            ImageQualityPreset.Mpx50 => new(50_331_648, 12288),
            ImageQualityPreset.Mpx12 => new(12_582_912, 6144),
            ImageQualityPreset.Mpx3 => new(2_764_800, 2880),
            _ => new(null, null),
        };

    public static ImageProcessRequest ToRequest(this ImageQualityPreset preset)
    {
        var placeholder = ImageOutputSpec.Placeholder();
        return preset switch {
            ImageQualityPreset.Original => new([ImageOutputSpec.Original(stripMetadata: true), placeholder]),
            ImageQualityPreset.OriginalWithExif => new([ImageOutputSpec.Original(stripMetadata: false), placeholder]),
            _ => new([ImageOutputSpec.Main(preset.GetBudget()), placeholder]),
        };
    }
}
