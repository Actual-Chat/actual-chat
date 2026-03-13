using ActualChat.UI.Services;

namespace ActualChat.UI.Blazor.Services;

public sealed class WebUploadStreamSource(IJSObjectReference jsRef) : IUploadStreamSource
{
    public IJSObjectReference JSRef { get; } = jsRef;
}
