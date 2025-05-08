using ActualChat.Contacts;

namespace ActualChat.UI.Blazor.App.Services;

public static class SpecialContact
{
    public static readonly Contact Unavailable = new(null!, 0) {
        Chat = SpecialChat.Unavailable,
    };
    public static readonly Contact Loading = new(null!, -1) {
        Chat = SpecialChat.Loading,
    };
}
