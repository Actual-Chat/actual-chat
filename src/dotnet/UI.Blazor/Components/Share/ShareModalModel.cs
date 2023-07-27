namespace ActualChat.UI.Blazor.Components;

public record ShareModalModel {
    public ShareModalModel(Uri link, string linkDescription = "") {
        Link = link;
        LinkDescription = linkDescription;
    }

    public string Text { get; } = "";
    public Uri? Link { get; }
    public string LinkDescription { get; } = "";
    public string Title { get; init; } = "";
}
