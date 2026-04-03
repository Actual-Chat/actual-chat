namespace ActualChat.Media;

[StructLayout(LayoutKind.Auto)]
public readonly record struct Size(int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}
