namespace ActualChat.UI.Blazor.App.Services;

public sealed record LocalLinkInfo(LocalUrl LocalUrl) {
    public Chat.Chat? Chat { get; init; }
    public ChatEntry? Entry { get; init; }
    public Author? Author { get; init; }
    public Place? Place { get; init; }
    public bool CanRender => Chat is not null;
}
