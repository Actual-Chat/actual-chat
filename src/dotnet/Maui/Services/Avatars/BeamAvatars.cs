using ActualLab.IO;
using SkiaSharp;

namespace ActualChat.Maui.Services;

/// <summary>
/// Generates beam-style PNG avatars with facial features using SkiaSharp.
/// </summary>
public static class BeamAvatars
{
    private const int DesignSize = 36;
    private const int Size = 80;
    private static readonly string[] DefaultColors = ["FFDBA0", "BBBEFF", "9294E1", "FF9BC0", "0F2FE8"];

    public static void GeneratePng(string key, FilePath filePath, int? size = null)
    {
        size ??= Size;
        var scale = (float)size.Value / DesignSize;
        var data = GenerateData(key, DefaultColors);
        var imageInfo = new SKImageInfo(size.Value, size.Value, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(imageInfo);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        canvas.Scale(scale);
        DrawBackground(canvas, data.BackgroundColor);
        DrawWrapper(canvas, data);
        DrawFace(canvas, data);

        using var pixmap = surface.PeekPixels();
        using var stream = new SKFileWStream(filePath);
        pixmap.Encode(stream, SKEncodedImageFormat.Png, 100);
    }

    // Private methods

    private static void DrawBackground(SKCanvas canvas, string colorHex)
    {
        using var paint = new SKPaint();
        paint.Color = ParseColor(colorHex);
        paint.IsAntialias = true;
        paint.Style = SKPaintStyle.Fill;

        canvas.DrawRect(new SKRect(0, 0, DesignSize, DesignSize), paint);
    }

    private static void DrawWrapper(SKCanvas canvas, AvatarData data)
    {
        using var paint = new SKPaint();
        paint.Color = ParseColor(data.WrapperColor);
        paint.IsAntialias = true;
        paint.Style = SKPaintStyle.Fill;

        canvas.Save();
        canvas.Translate((float)data.WrapperTranslateX, (float)data.WrapperTranslateY);
        canvas.RotateDegrees(data.WrapperRotate, DesignSize / 2f, DesignSize / 2f);
        canvas.Scale((float)data.WrapperScale);

        if (data.IsCircle)
            canvas.DrawCircle(DesignSize / 2f, DesignSize / 2f, DesignSize / 2f, paint);
        else {
            var cornerRadius = DesignSize / 6f;
            canvas.DrawRoundRect(new SKRect(0, 0, DesignSize, DesignSize), cornerRadius, cornerRadius, paint);
        }

        canvas.Restore();
    }

    private static void DrawFace(SKCanvas canvas, AvatarData data)
    {
        using var paint = new SKPaint();
        paint.Color = ParseColor(data.FaceColor);
        paint.IsAntialias = true;
        paint.Style = SKPaintStyle.Fill;

        canvas.Save();
        canvas.Translate((float)data.FaceTranslateX, (float)data.FaceTranslateY);
        canvas.RotateDegrees(data.FaceRotate, DesignSize / 2f, DesignSize / 2f);

        // Draw eyes
        var eyeWidth = 1.5f;
        var eyeHeight = 2f;
        var eyeCornerRadius = 1f;

        var leftEyeX = 14 - data.EyeSpread;
        canvas.DrawRoundRect(new SKRect(leftEyeX, 14, leftEyeX + eyeWidth, 14 + eyeHeight), eyeCornerRadius, eyeCornerRadius, paint);

        var rightEyeX = 20 + data.EyeSpread;
        canvas.DrawRoundRect(new SKRect(rightEyeX, 14, rightEyeX + eyeWidth, 14 + eyeHeight), eyeCornerRadius, eyeCornerRadius, paint);

        DrawMouth(canvas, data, paint);

        canvas.Restore();
    }

    private static void DrawMouth(SKCanvas canvas, AvatarData data, SKPaint paint)
    {
        var mouthY = 19 + data.MouthSpread;

        if (data.IsMouthOpen) {
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 1f;
            paint.StrokeCap = SKStrokeCap.Round;

            using var path = new SKPath();
            path.MoveTo(15, mouthY);
            path.QuadTo(18, mouthY + 1, 21, mouthY);
            canvas.DrawPath(path, paint);
        }
        else {
            paint.Style = SKPaintStyle.Fill;

            using var path = new SKPath();
            path.MoveTo(13, mouthY);
            path.ArcTo(new SKRect(13, mouthY - 0.75f, 23, mouthY + 0.75f), 180, -180, false);
            path.Close();
            canvas.DrawPath(path, paint);
        }
    }

    private static AvatarData GenerateData(string key, string[] colors)
    {
        var numFromName = AvatarUtils.HashCode(key);
        var range = colors.Length;
        var wrapperColor = AvatarUtils.GetRandomColor(numFromName, colors, range);
        var preTranslateX = AvatarUtils.GetUnit(numFromName, 10, 1);
        var wrapperTranslateX = preTranslateX < 5 ? preTranslateX + DesignSize / 9.0 : preTranslateX;
        var preTranslateY = AvatarUtils.GetUnit(numFromName, 10, 2);
        var wrapperTranslateY = preTranslateY < 5 ? preTranslateY + DesignSize / 9.0 : preTranslateY;

        return new AvatarData {
            WrapperColor = wrapperColor,
            FaceColor = AvatarUtils.GetContrast(wrapperColor),
            BackgroundColor = AvatarUtils.GetRandomColor(numFromName + 13, colors, range),
            WrapperTranslateX = wrapperTranslateX,
            WrapperTranslateY = wrapperTranslateY,
            WrapperRotate = AvatarUtils.GetUnit(numFromName, 360),
            WrapperScale = 1 + AvatarUtils.GetUnit(numFromName, DesignSize / 12) / 10.0,
            IsMouthOpen = AvatarUtils.GetBoolDigit(numFromName, 2),
            IsCircle = AvatarUtils.GetBoolDigit(numFromName, 1),
            EyeSpread = AvatarUtils.GetUnit(numFromName, 5),
            MouthSpread = AvatarUtils.GetUnit(numFromName, 3),
            FaceRotate = AvatarUtils.GetUnit(numFromName, 10, 3),
            FaceTranslateX = wrapperTranslateX > DesignSize / 6.0 ? wrapperTranslateX / 2 : AvatarUtils.GetUnit(numFromName, 8, 1),
            FaceTranslateY = wrapperTranslateY > DesignSize / 6.0 ? wrapperTranslateY / 2 : AvatarUtils.GetUnit(numFromName, 7, 2),
        };
    }

    private static SKColor ParseColor(string hex)
    {
        if (hex.OrdinalStartsWith("#"))
            hex = hex[1..];

        var r = byte.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var g = byte.Parse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var b = byte.Parse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        return new SKColor(r, g, b);
    }

    // Nested types

    /// <summary>
    /// Generated properties for a beam-style avatar.
    /// </summary>
    private record AvatarData
    {
        public required string WrapperColor { get; init; }
        public required string FaceColor { get; init; }
        public required string BackgroundColor { get; init; }
        public required double WrapperTranslateX { get; init; }
        public required double WrapperTranslateY { get; init; }
        public required int WrapperRotate { get; init; }
        public required double WrapperScale { get; init; }
        public required bool IsMouthOpen { get; init; }
        public required bool IsCircle { get; init; }
        public required int EyeSpread { get; init; }
        public required int MouthSpread { get; init; }
        public required int FaceRotate { get; init; }
        public required double FaceTranslateX { get; init; }
        public required double FaceTranslateY { get; init; }
    }
}
