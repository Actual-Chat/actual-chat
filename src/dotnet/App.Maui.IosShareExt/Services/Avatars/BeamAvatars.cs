namespace ActualChat.App.Maui.IosShareExt.Services;

public static class BeamAvatars
{
    private const int SIZE = 36;
    private static readonly string[] DefaultColors = ["FFDBA0", "BBBEFF", "9294E1", "FF9BC0", "0F2FE8"];
    private static int _maskIdCounter = 0;

    // TODO: optimize
    public static string GenerateSvg(string key, bool square = false)
    {
        var maskId = $"beam-avatar-{++_maskIdCounter}";
        var data = GenerateData(key, DefaultColors);
        var mouth = GetMouthSvg(data);

        // Use InvariantCulture to ensure decimal points are always "." regardless of locale
        return string.Create(CultureInfo.InvariantCulture, $@"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 {SIZE} {SIZE}' fill='none' role='img' width='100%' height='100%' preserveAspectRatio='none'>
    <mask id='{maskId}' maskUnits='userSpaceOnUse' x='0' y='0' width='{SIZE}' height='{SIZE}'>
        <rect width='{SIZE}' height='{SIZE}' rx='{(square ? "0" : (SIZE * 2).ToString())}' fill='#FFFFFF' />
    </mask>
    <g mask='url(#{maskId})'>
        <rect width='{SIZE}' height='{SIZE}' fill='#{data.BackgroundColor}' />
        <rect x='0' y='0' width='{SIZE}' height='{SIZE}' transform='translate({data.WrapperTranslateX} {data.WrapperTranslateY}) rotate({data.WrapperRotate} {SIZE / 2} {SIZE / 2}) scale({data.WrapperScale})' fill='#{data.WrapperColor}' rx='{(data.IsCircle ? SIZE : SIZE / 6)}' />
        <g transform='translate({data.FaceTranslateX} {data.FaceTranslateY}) rotate({data.FaceRotate} {SIZE / 2} {SIZE / 2})'>
            {mouth}
            <rect x='{14 - data.EyeSpread}' y='14' width='1.5' height='2' rx='1' stroke='none' fill='#{data.FaceColor}' />
            <rect x='{20 + data.EyeSpread}' y='14' width='1.5' height='2' rx='1' stroke='none' fill='#{data.FaceColor}' />
        </g>
    </g>
</svg>");
    }

    private static string GetMouthSvg(AvatarData data)
    {
        if (data.IsMouthOpen)
        {
            return $"<path d='M15 {19 + data.MouthSpread}c2 1 4 1 6 0' stroke='#{data.FaceColor}' fill='none' stroke-linecap='round' />";
        }
        else
        {
            return $"<path d='M13,{19 + data.MouthSpread} a1,0.75 0 0,0 10,0' fill='#{data.FaceColor}' />";
        }
    }

    private static AvatarData GenerateData(string key, string[] colors)
    {
        var numFromName = AvatarUtils.HashCode(key);
        var range = colors.Length;
        var wrapperColor = AvatarUtils.GetRandomColor(numFromName, colors, range);
        var preTranslateX = AvatarUtils.GetUnit(numFromName, 10, 1);
        var wrapperTranslateX = preTranslateX < 5 ? preTranslateX + SIZE / 9.0 : preTranslateX;
        var preTranslateY = AvatarUtils.GetUnit(numFromName, 10, 2);
        var wrapperTranslateY = preTranslateY < 5 ? preTranslateY + SIZE / 9.0 : preTranslateY;

        return new AvatarData
        {
            WrapperColor = wrapperColor,
            FaceColor = AvatarUtils.GetContrast(wrapperColor),
            BackgroundColor = AvatarUtils.GetRandomColor(numFromName + 13, colors, range),
            WrapperTranslateX = wrapperTranslateX,
            WrapperTranslateY = wrapperTranslateY,
            WrapperRotate = AvatarUtils.GetUnit(numFromName, 360),
            WrapperScale = 1 + AvatarUtils.GetUnit(numFromName, SIZE / 12) / 10.0,
            IsMouthOpen = AvatarUtils.GetBoolDigit(numFromName, 2),
            IsCircle = AvatarUtils.GetBoolDigit(numFromName, 1),
            EyeSpread = AvatarUtils.GetUnit(numFromName, 5),
            MouthSpread = AvatarUtils.GetUnit(numFromName, 3),
            FaceRotate = AvatarUtils.GetUnit(numFromName, 10, 3),
            FaceTranslateX = wrapperTranslateX > SIZE / 6.0 ? wrapperTranslateX / 2 : AvatarUtils.GetUnit(numFromName, 8, 1),
            FaceTranslateY = wrapperTranslateY > SIZE / 6.0 ? wrapperTranslateY / 2 : AvatarUtils.GetUnit(numFromName, 7, 2),
        };
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
