using ActualChat.Chat;

namespace ActualChat.UI.Blazor.Components;

public interface IMediaShareUI
{
    Task Share(ChatEntryAttachment attachment);
}
