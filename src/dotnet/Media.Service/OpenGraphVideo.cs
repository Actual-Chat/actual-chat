namespace ActualChat.Media;

public sealed record OpenGraphVideo
{
    public static readonly OpenGraphVideo None = new ();

    public string SecureUrl { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
}
