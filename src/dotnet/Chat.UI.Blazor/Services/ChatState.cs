using ActualChat.Contacts;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public sealed record ChatState(
    ChatInfo Info,
    ChatMediaState MediaState = default,
    bool IsSelected = false,
    Presence Presence = Presence.Unknown
    ) : IHasId<ChatId>
{
    public static ChatState None { get; } = new(ChatInfo.None);
    public static ChatState Loading { get; } = new(ChatInfo.Loading);

    // Shortcuts
    public ChatId Id => Info.Id;
    public Chat Chat => Info.Chat;
    public ChatNews News => Info.News;
    public Contact Contact => Info.Contact;
    public bool IsListening => MediaState.IsListening;
    public bool IsPlayingHistorical => MediaState.IsPlayingHistorical;
    public bool IsRecording => MediaState.IsRecording;
}
