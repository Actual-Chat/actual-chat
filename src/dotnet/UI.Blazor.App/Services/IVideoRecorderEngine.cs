namespace ActualChat.UI.Blazor.App.Services;

public interface IVideoRecorderEngine
{
    bool IsNativeCapture { get; }
    Task<bool> Start(ChatId chatId, CancellationToken ct = default);
    Task<bool> Stop(CancellationToken ct = default);
}
