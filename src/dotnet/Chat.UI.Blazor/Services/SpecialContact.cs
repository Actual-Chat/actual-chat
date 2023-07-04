using ActualChat.Contacts;

namespace ActualChat.Chat.UI.Blazor.Services;

public static class SpecialContact
{
    public static Contact Unavailable { get; } = new(default, 0) {
        Chat = SpecialChat.Unavailable,
    };
    public static Contact Loading { get; } = new(default, -1) {
        Chat = SpecialChat.Loading,
    };
}
