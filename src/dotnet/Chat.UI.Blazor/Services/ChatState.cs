using ActualChat.Contacts;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.Services;

public sealed record ChatState(
    ChatSummary Summary,
    bool IsSelected = false,
    bool IsListening = false,
    bool IsRecording = false,
    Presence Presence = Presence.Unknown)
{
    public static ChatState None { get; } = new(ChatSummary.None);
    public static ChatState Loading { get; } = new(ChatSummary.Loading);

    // Shortcuts
    public Contact Contact => Summary.Contact;
    public Chat Chat => Summary.Chat;
}
