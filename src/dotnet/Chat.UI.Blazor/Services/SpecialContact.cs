using ActualChat.Contacts;

namespace ActualChat.Chat.UI.Blazor.Services;

public static class SpecialContact
{
    public static readonly Contact Unavailable = new(default, 0) {
        Chat = SpecialChat.Unavailable,
    };
    public static readonly Contact Loading = new(default, -1) {
        Chat = SpecialChat.Loading,
    };
}
