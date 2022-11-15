namespace ActualChat.Chat.UI.Blazor.Services;

public record SingleChatPlaybackState(
    ChatId ChatId,
    bool IsListening,
    bool IsPlayingHistorical)
{
    public static SingleChatPlaybackState None { get; } = new(default, false, false);
}
