using ActualChat.UI.Services;

namespace ActualChat.UI.Blazor.Services;

public class WebUploadStreamSource(IJSObjectReference jsRef) : IUploadStreamSource
{
    public IJSObjectReference JSRef { get; } = jsRef;
}
