using ActualChat.Video;

namespace ActualChat;

/// <summary>
/// JS-interop snapshot of all client-relevant <see cref="Constants"/> sections.
/// Mirrored in TS as <c>AppConstants</c> in <c>app-constants.ts</c>; consumed
/// via the <c>APP_CONSTANTS</c> field (and per-section shortcuts like <c>VIDEO</c>).
/// </summary>
public sealed record AppConstants(VideoConstants Video)
{
    public static AppConstants Instance { get; } = new(VideoConstants.Default);
}
