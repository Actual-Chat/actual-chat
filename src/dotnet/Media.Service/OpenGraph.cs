namespace ActualChat.Media;

public sealed record OpenGraph(string Title)
{
    public static readonly OpenGraph None = new ("");
    public string ImageUrl { get; init; } = "";
    public string Description { get; init; } = "";
    public string SiteName { get; init; } = "";

    public OpenGraphVideo Video { get; init; } = OpenGraphVideo.None;
}
