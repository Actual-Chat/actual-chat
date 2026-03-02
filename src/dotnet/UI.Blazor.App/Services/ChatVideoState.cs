namespace ActualChat.UI.Blazor.App.Services;

public sealed record ChatVideoState(
    ChatId? ChatId,
    bool IsRecording = false,
    string? SelectedCameraDeviceId = null,
    bool IsBackgroundBlurEnabled = false,
    bool HasError = false,
    string? ErrorMessage = null)
{
    public static readonly ChatVideoState None = new((ChatId?)null);
}
