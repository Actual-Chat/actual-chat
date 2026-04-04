namespace ActualChat.UI.Blazor;

public interface IJSRuntimeWrapper : IJSRuntime
{
    IJSRuntime WrappedJSRuntime { get; }
}
