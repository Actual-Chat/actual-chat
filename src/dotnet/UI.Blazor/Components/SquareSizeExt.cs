namespace ActualChat.UI.Blazor.Components;

public static class SquareSizeExt
{
    public static string GetCssClass(this SquareSize squareSize)
    {
        var size = (int)squareSize;
        return $"h-{size} w-{size}";
    }
}
