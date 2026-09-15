using ActualChat.Live;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Mirrors an outgoing call into the platform's own call UI (CallKit), so the caller
/// and the callee end up on one audio-session regime and the call reaches the system
/// call log. Inert on platforms without one (Android, web).
/// </summary>
public interface ISystemCallUI
{
    void OnOutgoingCallStarted(ChatId chatId, bool hasVideo);
    void OnOutgoingCallStatusChanged(ChatId chatId, CallStatus status);
    void OnOutgoingCallCancelled(ChatId chatId);
}

public sealed class DefaultSystemCallUI : ISystemCallUI
{
    public void OnOutgoingCallStarted(ChatId chatId, bool hasVideo)
    { }

    public void OnOutgoingCallStatusChanged(ChatId chatId, CallStatus status)
    { }

    public void OnOutgoingCallCancelled(ChatId chatId)
    { }
}
