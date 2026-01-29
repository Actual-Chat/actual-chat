using SkiaSharp;

namespace ActualChat.App.Maui.IosShareExt.Services;

public static class BeamAvatars
{
    private const int Size = 36;
    private static readonly string[] DefaultColors = ["FFDBA0", "BBBEFF", "9294E1", "FF9BC0", "0F2FE8"];

    public static byte[] GeneratePng(string key)
    {
        var data = GenerateData(key, DefaultColors);
        var imageInfo = new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(imageInfo);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        // Draw background
        DrawBackground(canvas, data.BackgroundColor);

        // Draw wrapper shape with transformations
        DrawWrapper(canvas, data);

        // Draw face (eyes and mouth) with transformations
        DrawFace(canvas, data);

        using var image = surface.Snapshot();
        using var pngData = image.Encode(SKEncodedImageFormat.Png, 100);
        return pngData.ToArray();
    }

    private static void DrawBackground(SKCanvas canvas, string colorHex)
    {
        using var paint = new SKPaint();
        paint.Color = ParseColor(colorHex);
        paint.IsAntialias = true;
        paint.Style = SKPaintStyle.Fill;

        canvas.DrawRect(new SKRect(0, 0, Size, Size), paint);
    }

    private static void DrawWrapper(SKCanvas canvas, AvatarData data)
    {
        using var paint = new SKPaint();
        paint.Color = ParseColor(data.WrapperColor);
        paint.IsAntialias = true;
        paint.Style = SKPaintStyle.Fill;

        canvas.Save();
        canvas.Translate((float)data.WrapperTranslateX, (float)data.WrapperTranslateY);
        canvas.RotateDegrees(data.WrapperRotate, Size / 2f, Size / 2f);
        canvas.Scale((float)data.WrapperScale);

        if (data.IsCircle)
        {
            canvas.DrawCircle(Size / 2f, Size / 2f, Size / 2f, paint);
        }
        else
        {
            var cornerRadius = Size / 6f;
            canvas.DrawRoundRect(new SKRect(0, 0, Size, Size), cornerRadius, cornerRadius, paint);
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
        canvas.RotateDegrees(data.FaceRotate, Size / 2f, Size / 2f);

        // Draw eyes (small rounded rectangles)
        var eyeWidth = 1.5f;
        var eyeHeight = 2f;
        var eyeCornerRadius = 1f;

        // Left eye
        var leftEyeX = 14 - data.EyeSpread;
        canvas.DrawRoundRect(new SKRect(leftEyeX, 14, leftEyeX + eyeWidth, 14 + eyeHeight), eyeCornerRadius, eyeCornerRadius, paint);

        // Right eye
        var rightEyeX = 20 + data.EyeSpread;
        canvas.DrawRoundRect(new SKRect(rightEyeX, 14, rightEyeX + eyeWidth, 14 + eyeHeight), eyeCornerRadius, eyeCornerRadius, paint);

        // Draw mouth
        DrawMouth(canvas, data, paint);

        canvas.Restore();
    }

    private static void DrawMouth(SKCanvas canvas, AvatarData data, SKPaint paint)
    {
        var mouthY = 19 + data.MouthSpread;

        if (data.IsMouthOpen)
        {
            // Open mouth: curved line stroke
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 1f;
            paint.StrokeCap = SKStrokeCap.Round;

            using var path = new SKPath();
            path.MoveTo(15, mouthY);
            path.QuadTo(18, mouthY + 1, 21, mouthY);
            canvas.DrawPath(path, paint);
        }
        else
        {
            // Closed mouth: filled arc/smile
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
        var wrapperTranslateX = preTranslateX < 5 ? preTranslateX + Size / 9.0 : preTranslateX;
        var preTranslateY = AvatarUtils.GetUnit(numFromName, 10, 2);
        var wrapperTranslateY = preTranslateY < 5 ? preTranslateY + Size / 9.0 : preTranslateY;

        return new AvatarData
        {
            WrapperColor = wrapperColor,
            FaceColor = AvatarUtils.GetContrast(wrapperColor),
            BackgroundColor = AvatarUtils.GetRandomColor(numFromName + 13, colors, range),
            WrapperTranslateX = wrapperTranslateX,
            WrapperTranslateY = wrapperTranslateY,
            WrapperRotate = AvatarUtils.GetUnit(numFromName, 360),
            WrapperScale = 1 + AvatarUtils.GetUnit(numFromName, Size / 12) / 10.0,
            IsMouthOpen = AvatarUtils.GetBoolDigit(numFromName, 2),
            IsCircle = AvatarUtils.GetBoolDigit(numFromName, 1),
            EyeSpread = AvatarUtils.GetUnit(numFromName, 5),
            MouthSpread = AvatarUtils.GetUnit(numFromName, 3),
            FaceRotate = AvatarUtils.GetUnit(numFromName, 10, 3),
            FaceTranslateX = wrapperTranslateX > Size / 6.0 ? wrapperTranslateX / 2 : AvatarUtils.GetUnit(numFromName, 8, 1),
            FaceTranslateY = wrapperTranslateY > Size / 6.0 ? wrapperTranslateY / 2 : AvatarUtils.GetUnit(numFromName, 7, 2),
        };
    }

    private static SKColor ParseColor(string hex)
    {
        if (hex.StartsWith("#", StringComparison.Ordinal))
            hex = hex[1..];

        var r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

        return new SKColor(r, g, b);
    }

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
