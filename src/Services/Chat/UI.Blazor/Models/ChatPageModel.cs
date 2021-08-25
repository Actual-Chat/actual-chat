namespace ActualChat.Chat.UI.Blazor.Models
{
    public record ChatPageModel
    {
        public bool IsUnavailable { get; init; }
        public bool MustLogin { get; init; }
    }
}
