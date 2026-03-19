namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Fallback video recorder engine for web platforms.
/// When IsNativeCapture is false, the Blazor component uses the existing JS path unchanged.
/// </summary>
public class JsVideoRecorderEngine : IVideoRecorderEngine
{
    public bool IsNativeCapture => false;

    public Task<bool> Start(ChatId chatId, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<bool> Stop(CancellationToken ct = default)
        => Task.FromResult(true);
}
