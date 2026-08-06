using ActualChat.Contacts;

namespace ActualChat.UI.Blazor.App.Services;

[ParameterComparer(typeof(ByRefParameterComparer))]
public sealed record ChatState(ChatInfo Info, ChatAudioState AudioState) : IHasId<ChatId>
{
    public bool IsSelected { get; init; }
    public Presence Presence { get; init; } = Presence.Unknown;

    // Shortcuts
    public ChatId Id => Info.Id;
    public Chat.Chat Chat => Info.Chat;
    public Contact Contact => Info.Contact;
    public bool IsListening => AudioState.IsListening;
    public bool IsReplaying => AudioState.IsReplaying;
    public bool IsRecording => AudioState.IsRecording;

    // This record relies on referential equality
    public bool Equals(ChatState? other) => ReferenceEquals(this, other);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
}
