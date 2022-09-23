namespace ActualChat.Chat.UI.Blazor.Services;

public record SingleChatPlaybackState(
    Symbol ChatId,
    bool IsListening,
    bool IsPlayingHistorical)
{
    public static SingleChatPlaybackState None { get; } = new(Symbol.Empty, false, false);
}
