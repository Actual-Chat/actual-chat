namespace ActualChat.Media;

// A visual style appended to the description a generated image is built from.
//
// Numbers are the wire format, so new members go on the end; ImageStyleExt.All orders the picker.
// Default and Random name no style of their own - Resolve() picks one per generation, so repeating
// a description does not repeat the image.

public enum ImageStyle
{
    None = 0,
    // Graphic and vector
    FlatVector = 1,
    LineArt = 2,
    Minimalist = 3,
    Risograph = 4,
    // Illustration and painterly
    Cartoon = 5,
    Anime = 6,
    ComicBook = 7,
    Watercolor = 8,
    OilPainting = 9,
    PencilSketch = 10,
    FantasyArt = 11,
    // Dimensional and material
    Isometric = 12,
    LowPoly = 13,
    Render3D = 14,
    Claymation = 15,
    Papercut = 16,
    StainedGlass = 17,
    PixelArt = 18,
    // Photographic and genre
    Photo = 19,
    Cinematic = 20,
    NeonCyberpunk = 21,
    Ghibli = 22,
    // Resolved, not rendered
    Default = 23,
    Random = 24,
}

public static class ImageStyleExt
{
    // The picker's order: the two resolved entries, then None, then the rest grouped by medium
    public static readonly IReadOnlyList<ImageStyle> All = [
        ImageStyle.Default,
        ImageStyle.Random,
        ImageStyle.None,
        // Graphic and vector
        ImageStyle.FlatVector,
        ImageStyle.LineArt,
        ImageStyle.Minimalist,
        ImageStyle.Risograph,
        // Illustration and painterly
        ImageStyle.Cartoon,
        ImageStyle.Anime,
        ImageStyle.Ghibli,
        ImageStyle.ComicBook,
        ImageStyle.Watercolor,
        ImageStyle.OilPainting,
        ImageStyle.PencilSketch,
        ImageStyle.FantasyArt,
        // Dimensional and material
        ImageStyle.Isometric,
        ImageStyle.LowPoly,
        ImageStyle.Render3D,
        ImageStyle.Claymation,
        ImageStyle.Papercut,
        ImageStyle.StainedGlass,
        ImageStyle.PixelArt,
        // Photographic and genre
        ImageStyle.Photo,
        ImageStyle.Cinematic,
        ImageStyle.NeonCyberpunk,
    ];

    // What Default draws from: the looks that suit a chat picture at avatar size. Narrower than
    // Random, which spans every concrete style including the ones that rarely read well that small.
    private static readonly ImageStyle[] DefaultPool = [
        ImageStyle.Cartoon,
        ImageStyle.Render3D,
        ImageStyle.Watercolor,
        ImageStyle.Isometric,
        ImageStyle.FantasyArt,
        ImageStyle.PixelArt,
        ImageStyle.LowPoly,
        ImageStyle.Ghibli,
    ];

    private static readonly ImageStyle[] RandomPool =
        All.Where(x => x is not (ImageStyle.None or ImageStyle.Default or ImageStyle.Random)).ToArray();

    // Shared by every style rather than repeated in each tail: the result is cropped to a circle, so
    // a subject inset in a margin loses its edges and a drawn border is sliced through.
    private const string Framing = "centered, fills the frame, plain background, no text, no watermark";
    private const string DarkFraming = "centered, fills the frame, dark plain background, no text, no watermark";

    // Every style but None gets this. The result is displayed at avatar size, and left to itself a
    // model paints a detailed scene and frames it - both wrong at 40px.
    private const string IconBrief =
        "one subject in few bold shapes, still readable at 40px, no fine detail, "
        + "no frame, no border, no vignette, no rounded-square badge";

    private static readonly IReadOnlyDictionary<ImageStyle, string> Tails =
        new Dictionary<ImageStyle, string> {
            [ImageStyle.None] = "",
            [ImageStyle.FlatVector] =
                "flat vector app-icon illustration, bold simple shapes, flat colors, crisp edges",
            [ImageStyle.LineArt] =
                "minimal single-weight line drawing, black ink on white, no shading",
            [ImageStyle.Minimalist] =
                "minimalist geometric emblem, two or three flat colors, generous negative space",
            [ImageStyle.Risograph] =
                "risograph print, two-color duotone inks, coarse halftone grain, slight misregistration",
            [ImageStyle.Cartoon] =
                "modern cartoon illustration, thick bouncy outlines, exaggerated friendly features, flat cel shading",
            [ImageStyle.Anime] =
                "anime key visual, clean cel shading, expressive eyes, vibrant saturated colors",
            [ImageStyle.Ghibli] =
                "Studio Ghibli style anime, hand-painted scenery, soft natural light, gentle warm palette",
            [ImageStyle.ComicBook] =
                "comic book ink art, heavy inked linework, halftone dot shading, bold primary colors",
            [ImageStyle.Watercolor] =
                "loose watercolor painting, soft wet washes, bleeding pigment, visible paper grain",
            [ImageStyle.OilPainting] =
                "classical oil painting, thick impasto brushstrokes, warm chiaroscuro lighting, muted earth palette",
            [ImageStyle.PencilSketch] =
                "graphite pencil sketch, hatched shading, smudged soft grays, sketchbook paper texture",
            [ImageStyle.FantasyArt] =
                "painterly fantasy art, ethereal rim light, ornate detail, rich jewel colors",
            [ImageStyle.Isometric] =
                "isometric miniature diorama, tidy crisp geometry, soft ambient occlusion shadows",
            [ImageStyle.LowPoly] =
                "low-poly 3D style, faceted triangular polygons, flat shaded facets",
            [ImageStyle.Render3D] =
                "Pixar-style 3D animation render, appealing rounded forms, believable materials, warm lighting",
            [ImageStyle.Claymation] =
                "handmade plasticine clay sculpture, visible fingerprints, matte modeling-compound texture",
            [ImageStyle.Papercut] =
                "layered cut-paper collage, stacked construction paper, torn edges, soft paper shadows",
            [ImageStyle.StainedGlass] =
                "stained glass window art, thick black leading, luminous jewel-tone glass panes, backlit",
            [ImageStyle.PixelArt] =
                "16-bit pixel art, chunky pixels, limited retro palette, hard aliased edges",
            [ImageStyle.Photo] =
                "studio photograph, 85mm lens, soft key light, shallow depth of field",
            [ImageStyle.Cinematic] =
                "cinematic film still, moody dramatic lighting, teal and amber color grade, soft film grain",
            [ImageStyle.NeonCyberpunk] =
                "neon cyberpunk art, magenta and cyan rim light, glowing reflections, high contrast",
        };

    // Turns Default and Random into the style this one generation renders in; everything else is
    // already concrete. Call it once per generation - calling it twice picks twice.
    public static ImageStyle Resolve(this ImageStyle style)
        => style switch {
            ImageStyle.Default => DefaultPool[Random.Shared.Next(DefaultPool.Length)],
            ImageStyle.Random => RandomPool[Random.Shared.Next(RandomPool.Length)],
            _ => style,
        };

    // Returns the template the image generator expects: "{0}" is replaced by the description.
    public static string GetPromptTemplate(this ImageStyle style, bool isBackground = false)
    {
        if (isBackground) {
            style = style == ImageStyle.Default ? ImageStyle.Photo : style.Resolve();
            var backgroundStyle = style == ImageStyle.FlatVector
                ? "flat vector illustration, bold shapes, flat colors, crisp edges"
                : Tails.GetValueOrDefault(style, "");
            return $"{{0}}. {backgroundStyle}, camera-style scene, spacious composition, natural depth, "
                + "fills the frame, no text, no watermark, no frame, no border.";
        }

        style = style.Resolve();
        var framing = style == ImageStyle.NeonCyberpunk ? DarkFraming : Framing;
        var tail = Tails.GetValueOrDefault(style, "");
        return tail.IsNullOrEmpty()
            ? $"{{0}}. {framing}."
            : $"{{0}}. {tail}, {IconBrief}, {framing}.";
    }
}
