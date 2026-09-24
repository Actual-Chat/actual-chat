using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Where a call's audio goes (<see cref="Kind"/>), and the connected headset it could go to instead.
/// </summary>
public sealed record CallAudioOutput(AudioOutputKind Kind, AudioOutputKind? ExternalKind);
