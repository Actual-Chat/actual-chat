using ActualChat.Contacts;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public sealed record ChatState(
    ChatInfo Info,
    ChatAudioState AudioState = default
    ) : IHasId<ChatId>
{
    public static readonly ChatState None = new(ChatInfo.None);
    public static readonly ChatState Loading = new(ChatInfo.Loading);

    public bool IsSelected { get; init; }
    public Presence Presence { get; init; } = Presence.Unknown;

    // Shortcuts
    public ChatId Id => Info.Id;
    public Chat Chat => Info.Chat;
    public Contact Contact => Info.Contact;
    public bool IsListening => AudioState.IsListening;
    public bool IsPlayingHistorical => AudioState.IsPlayingHistorical;
    public bool IsRecording => AudioState.IsRecording;
}
