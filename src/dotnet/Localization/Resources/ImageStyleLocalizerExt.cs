using ActualChat.Media;
using Microsoft.Extensions.Localization;

namespace ActualChat.Localization;

/// <summary>
/// Display names for <see cref="ImageStyle"/>, so a picker never renders one with ToString().
/// </summary>
public static class ImageStyleLocalizerExt
{
    // Deliberately not inside LocalizedStringsLocalizerExt: AppLocalizationTest requires that block
    // to hold exactly one member per catalog key, and this maps many keys through one member.
    public static string ImageStyleName(this IStringLocalizer l, ImageStyle style)
        => style switch {
            ImageStyle.Default => l.ImageStyle_Default,
            ImageStyle.Random => l.ImageStyle_Random,
            ImageStyle.None => l.ImageStyle_None,
            ImageStyle.FlatVector => l.ImageStyle_FlatVector,
            ImageStyle.LineArt => l.ImageStyle_LineArt,
            ImageStyle.Minimalist => l.ImageStyle_Minimalist,
            ImageStyle.Risograph => l.ImageStyle_Risograph,
            ImageStyle.Cartoon => l.ImageStyle_Cartoon,
            ImageStyle.Anime => l.ImageStyle_Anime,
            ImageStyle.Ghibli => l.ImageStyle_Ghibli,
            ImageStyle.ComicBook => l.ImageStyle_ComicBook,
            ImageStyle.Watercolor => l.ImageStyle_Watercolor,
            ImageStyle.OilPainting => l.ImageStyle_OilPainting,
            ImageStyle.PencilSketch => l.ImageStyle_PencilSketch,
            ImageStyle.FantasyArt => l.ImageStyle_FantasyArt,
            ImageStyle.Isometric => l.ImageStyle_Isometric,
            ImageStyle.LowPoly => l.ImageStyle_LowPoly,
            ImageStyle.Render3D => l.ImageStyle_Render3D,
            ImageStyle.Claymation => l.ImageStyle_Claymation,
            ImageStyle.Papercut => l.ImageStyle_Papercut,
            ImageStyle.StainedGlass => l.ImageStyle_StainedGlass,
            ImageStyle.PixelArt => l.ImageStyle_PixelArt,
            ImageStyle.Photo => l.ImageStyle_Photo,
            ImageStyle.Cinematic => l.ImageStyle_Cinematic,
            ImageStyle.NeonCyberpunk => l.ImageStyle_NeonCyberpunk,
            _ => l.ImageStyle_Default,
        };
}
