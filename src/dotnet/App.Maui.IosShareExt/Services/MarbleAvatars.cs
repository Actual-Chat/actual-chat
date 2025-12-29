namespace ActualChat.App.Maui.IosShareExt.Services;

public static class MarbleAvatars
{
    private const int SIZE = 80;
    private const int ELEMENTS = 3;
    private static readonly string[] DefaultColors = ["F56095", "F5CD65", "00B27D", "37D3F5", "2F89EB"];
    private static int _maskIdCounter = 0;

    public static string GenerateSvg(string key, string title, bool doNotBlur = false)
    {
        var maskId = $"marble-avatar-{++_maskIdCounter}";
        var properties = GenerateColors(key, DefaultColors);
        var blurEffect = GetBlurEffect(doNotBlur);

        return $@"<svg viewBox='0 0 {SIZE} {SIZE}' fill='none' role='img' xmlns='http://www.w3.org/2000/svg' preserveAspectRatio='none' width='100%' height='100%'>
    <mask id='{maskId}' maskUnits='userSpaceOnUse' x='0' y='0' width='{SIZE}' height='{SIZE}'>
        <rect width='{SIZE}' height='{SIZE}' fill='#FFFFFF' />
    </mask>
    <g mask='url(#{maskId})'>
        <rect width='{SIZE}' height='{SIZE}' fill='#{properties[0].Color}' />
        <path filter='url(#prefix__filter0_f)' d='M32.414 59.35L50.376 70.5H72.5v-71H33.728L26.5 13.381l19.057 27.08L32.414 59.35z' fill='#{properties[1].Color}' transform='translate({properties[1].TranslateX} {properties[1].TranslateY}) rotate({properties[1].Rotate} {SIZE / 2} {SIZE / 2}) scale({properties[2].Scale})' />
        <path filter='url(#prefix__filter0_f)' style='mix-blend-mode: overlay;' d='M22.216 24L0 46.75l14.108 38.129L78 86l-3.081-59.276-22.378 4.005 12.972 20.186-23.35 27.395L22.215 24z' fill='#{properties[2].Color}' transform='translate({properties[2].TranslateX} {properties[2].TranslateY}) rotate({properties[2].Rotate} {SIZE / 2} {SIZE / 2}) scale({properties[2].Scale})' />
    </g>
    <text x='50%' y='50%' dominant-baseline='central' text-anchor='middle' font-size='2.5em' font-weight='500' fill='white'>{title}</text>
    <defs>
        <filter id='prefix__filter0_f' filterUnits='userSpaceOnUse' color-interpolation-filters='sRGB'>
            <feFlood flood-opacity='0' result='BackgroundImageFix' />
            <feBlend in='SourceGraphic' in2='BackgroundImageFix' result='shape' />
            {blurEffect}
        </filter>
    </defs>
</svg>";
    }

    private static ColorProperty[] GenerateColors(string key, string[] colors)
    {
        var numFromName = AvatarUtils.HashCode(key);
        var range = colors.Length;
        return Enumerable.Range(0, ELEMENTS)
            .Select(i => new ColorProperty
            {
                Color = AvatarUtils.GetRandomColor(numFromName + i, colors, range),
                TranslateX = AvatarUtils.GetUnit(numFromName * (i + 1), SIZE / 10, 1),
                TranslateY = AvatarUtils.GetUnit(numFromName * (i + 1), SIZE / 10, 2),
                Scale = 1.2 + AvatarUtils.GetUnit(numFromName * (i + 1), SIZE / 20) / 10.0,
                Rotate = AvatarUtils.GetUnit(numFromName * (i + 1), 360, 1),
            })
            .ToArray();
    }

    private static string GetBlurEffect(bool doNotBlur)
    {
        return doNotBlur ? "" : "<feGaussianBlur stdDeviation='7' result='effect1_foregroundBlur' />";
    }

    private record ColorProperty
    {
        public required string Color { get; init; }
        public required double TranslateX { get; init; }
        public required double TranslateY { get; init; }
        public required double Scale { get; init; }
        public required int Rotate { get; init; }
    }

}
