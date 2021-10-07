namespace ActualChat.Chat.UI.Blazor.Pages
{
    public record ChatPageModel
    {
        public bool IsUnavailable { get; init; }
        public bool MustLogin { get; init; }
        public Chat? Chat { get; init; }
    }
}
