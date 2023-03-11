namespace ActualChat.Chat.UI.Blazor.Components;

public class NullNoMicHandler : INoMicHandler
{
    public Task Allow()
        => throw StandardError.NotSupported("Can not handle Allow request");
}
