namespace ActualChat.UI.Blazor.App.Services;

public sealed record WebAudioContextDiagnostics(
    string Purpose,
    string State,
    double? SampleRate,
    double? BaseLatencyMs,
    double? OutputLatencyMs,
    bool IsRunning,
    bool IsMaintained,
    bool IsUsed,
    int RefCount,
    bool IsReady,
    string? BackgroundActivity);
