using ActualLab.IO;
using SkiaSharp;

namespace ActualChat.Users.AvatarIcons;

public static class MarbleAvatars
{
    private const int Size = 80;
    private const int Elements = 3;
    private const float BlurSigma = 7f;
    private const float FontSize = Size * 0.5f;
    private const string TitleFontResourceName = "TT-Commons-Pro-Medium.ttf";
    private const string BasePathData = "M32.414 59.35L50.376 70.5H72.5v-71H33.728L26.5 13.381l19.057 27.08L32.414 59.35z";
    private const string OverlayPathData = "M22.216 24L0 46.75l14.108 38.129L78 86l-3.081-59.276-22.378 4.005 12.972 20.186-23.35 27.395L22.215 24z";
    private static readonly string[] DefaultColors = ["F56095", "F5CD65", "00B27D", "37D3F5", "2F89EB"];
    private static readonly SKTypeface TitleTypeface = LoadTitleTypeface();
    private static ILogger? _log;

    private static ILogger Log => _log ??= StaticLog.Factory.CreateLogger(typeof(MarbleAvatars));

    public static void GeneratePng(string key, FilePath filePath, string title = "", bool doNotBlur = false, int? size = null)
    {
        size ??= Size;
        var scale = (float)size.Value / Size;
        var properties = GenerateColors(key, DefaultColors);
        var imageInfo = new SKImageInfo(size.Value, size.Value, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(imageInfo);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        canvas.Scale(scale);
        canvas.Save();
        canvas.ClipRect(new SKRect(0, 0, Size, Size), SKClipOperation.Intersect, true);

        var scaledBlurSigma = BlurSigma * scale;
        using var blurFilter = doNotBlur ? null : SKImageFilter.CreateBlur(scaledBlurSigma, scaledBlurSigma);

        DrawBackground(canvas, properties[0]);

        using var basePath = SKPath.ParseSvgPathData(BasePathData);
        DrawPath(canvas, basePath, properties[1], properties[2].Scale, blurFilter, SKBlendMode.SrcOver);

        using var overlayPath = SKPath.ParseSvgPathData(OverlayPathData);
        DrawPath(canvas, overlayPath, properties[2], properties[2].Scale, blurFilter, SKBlendMode.Overlay);

        DrawTitle(canvas, title);

        canvas.Restore();

        using var pixmap = surface.PeekPixels();
        using var stream = new SKFileWStream(filePath);
        pixmap.Encode(stream, SKEncodedImageFormat.Png, 100);
    }

    public static byte[] GeneratePngBytes(string key, int size = Size, string[]? colors = null, string title = "", bool doNotBlur = false)
    {
        colors ??= DefaultColors;
        var scale = (float)size / Size;
        var properties = GenerateColors(key, colors);
        var imageInfo = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(imageInfo);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        canvas.Scale(scale);
        canvas.Save();
        canvas.ClipRect(new SKRect(0, 0, Size, Size), SKClipOperation.Intersect, true);

        var scaledBlurSigma = BlurSigma * scale;
        using var blurFilter = doNotBlur ? null : SKImageFilter.CreateBlur(scaledBlurSigma, scaledBlurSigma);

        DrawBackground(canvas, properties[0]);

        using var basePath = SKPath.ParseSvgPathData(BasePathData);
        DrawPath(canvas, basePath, properties[1], properties[2].Scale, blurFilter, SKBlendMode.SrcOver);

        using var overlayPath = SKPath.ParseSvgPathData(OverlayPathData);
        DrawPath(canvas, overlayPath, properties[2], properties[2].Scale, blurFilter, SKBlendMode.Overlay);

        DrawTitle(canvas, title);

        canvas.Restore();

        using var image = surface.Snapshot();
        using var pngData = image.Encode(SKEncodedImageFormat.Png, 100);
        return pngData.ToArray();
    }

    public static string GenerateSvg(string key, string[]? colors = null, string title = "", bool doNotBlur = false)
    {
        colors ??= DefaultColors;
        var properties = GenerateColors(key, colors);

        var blurEffect = doNotBlur
            ? ""
            : "<feGaussianBlur stdDeviation='7' result='effect1_foregroundBlur' />";

        var displayTitle = title.IsNullOrEmpty() ? "" : title[0].ToString().ToUpper().HtmlEncode();

        return $"""
            <svg viewBox='0 0 {Size} {Size}' fill='none' xmlns='http://www.w3.org/2000/svg' width='{Size}' height='{Size}'>
                <mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='{Size}' height='{Size}'>
                    <rect width='{Size}' height='{Size}' fill='#FFFFFF' />
                </mask>
                <g mask='url(#m)'>
                    <rect width='{Size}' height='{Size}' fill='#{properties[0].Color}' />
                    <path filter='url(#f)' d='{BasePathData}' fill='#{properties[1].Color}' transform='translate({properties[1].TranslateX} {properties[1].TranslateY}) rotate({properties[1].Rotate} {Size / 2} {Size / 2}) scale({properties[2].Scale:F2})' />
                    <path filter='url(#f)' style='mix-blend-mode: overlay;' d='{OverlayPathData}' fill='#{properties[2].Color}' transform='translate({properties[2].TranslateX} {properties[2].TranslateY}) rotate({properties[2].Rotate} {Size / 2} {Size / 2}) scale({properties[2].Scale:F2})' />
                </g>
                <text x='50%' y='50%' dominant-baseline='central' text-anchor='middle' font-family='TT Commons Pro, sans-serif' font-size='2.5em' font-weight='500' fill='white'>{displayTitle}</text>
                <defs>
                    <filter id='f' filterUnits='userSpaceOnUse' color-interpolation-filters='sRGB'>
                        <feFlood flood-opacity='0' result='BackgroundImageFix' />
                        <feBlend in='SourceGraphic' in2='BackgroundImageFix' result='shape' />
                        {blurEffect}
                    </filter>
                </defs>
            </svg>
            """;
    }

    // Private methods

    private static void DrawBackground(SKCanvas canvas, ColorProperty background)
    {
        using var paint = new SKPaint();
        paint.Color = ParseColor(background.Color);
        paint.IsAntialias = true;
        paint.Style = SKPaintStyle.Fill;

        canvas.DrawRect(new SKRect(0, 0, Size, Size), paint);
    }

    private static void DrawPath(
        SKCanvas canvas,
        SKPath path,
        ColorProperty property,
        double scale,
        SKImageFilter? blurFilter,
        SKBlendMode blendMode)
    {
        using var paint = new SKPaint();
        paint.Color = ParseColor(property.Color);
        paint.IsAntialias = true;
        paint.Style = SKPaintStyle.Fill;
        paint.ImageFilter = blurFilter;
        paint.BlendMode = blendMode;

        canvas.Save();
        canvas.Translate((float)property.TranslateX, (float)property.TranslateY);
        canvas.RotateDegrees(property.Rotate, Size / 2f, Size / 2f);
        var scaleValue = (float)scale;
        canvas.Scale(scaleValue, scaleValue);
        canvas.DrawPath(path, paint);
        canvas.Restore();
    }

    private static SKTypeface LoadTitleTypeface()
    {
        try {
            using var stream = typeof(MarbleAvatars).Assembly.GetManifestResourceStream(TitleFontResourceName);
            if (stream is not null && SKTypeface.FromStream(stream) is { } typeface)
                return typeface;

            Log.LogWarning("'{Resource}' isn't embedded - avatar titles will use the host's default font",
                TitleFontResourceName);
        }
        catch (Exception e) {
            Log.LogWarning(e, "Failed to load '{Resource}' - avatar titles will use the host's default font",
                TitleFontResourceName);
        }

        var fontStyle = new SKFontStyle(SKFontStyleWeight.Medium, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        return SKTypeface.FromFamilyName(null, fontStyle) ?? SKTypeface.Default;
    }

    private static void DrawTitle(SKCanvas canvas, string title)
    {
        using var paint = new SKPaint();
        paint.Color = SKColors.White;
        paint.IsAntialias = true;

        using var font = new SKFont(TitleTypeface, FontSize);

        var metrics = font.Metrics;
        var x = Size / 2f;
        var y = (Size - (metrics.Ascent + metrics.Descent)) / 2f;
        canvas.DrawText(title, x, y, SKTextAlign.Center, font, paint);
    }

    private static ColorProperty[] GenerateColors(string key, string[] colors)
    {
        var numFromName = (long)AvatarUtils.HashCode(key);
        var range = colors.Length;
        return Enumerable.Range(0, Elements)
            .Select(i => new ColorProperty {
                Color = AvatarUtils.GetRandomColor((int)(numFromName + i), colors, range),
                TranslateX = AvatarUtils.GetUnit(numFromName * (i + 1), Size / 10, 1),
                TranslateY = AvatarUtils.GetUnit(numFromName * (i + 1), Size / 10, 2),
                Scale = 1.2 + (AvatarUtils.GetUnit(numFromName * (i + 1), Size / 20) / 10.0),
                Rotate = AvatarUtils.GetUnit(numFromName * (i + 1), 360, 1),
            })
            .ToArray();
    }

    private static SKColor ParseColor(string hex)
    {
        if (hex.StartsWith('#'))
            hex = hex[1..];

        if (hex.Length != 6 && hex.Length != 8)
            throw new ArgumentException("Expected a 6- or 8-digit hex color.", nameof(hex));

        var r = byte.Parse(hex[..2], NumberStyles.HexNumber);
        var g = byte.Parse(hex[2..4], NumberStyles.HexNumber);
        var b = byte.Parse(hex[4..6], NumberStyles.HexNumber);
        var a = hex.Length == 8
            ? byte.Parse(hex[6..8], NumberStyles.HexNumber)
            : (byte)255;

        return new SKColor(r, g, b, a);
    }

    // Nested types

    private sealed record ColorProperty
    {
        public required string Color { get; init; }
        public required double TranslateX { get; init; }
        public required double TranslateY { get; init; }
        public required double Scale { get; init; }
        public required int Rotate { get; init; }
    }
}
