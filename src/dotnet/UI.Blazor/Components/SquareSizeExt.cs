namespace ActualChat.UI.Blazor.Components;

public static class SquareSizeExt
{
    public static string GetHeightCssClass(this SquareSize size)
        => $"h-{size:D}";

    public static string GetWidthCssClass(this SquareSize size)
        => $"w-{size:D}";

    public static string GetCssClass(this SquareSize size)
        => $"{size.GetHeightCssClass()} {size.GetWidthCssClass()}";
}
