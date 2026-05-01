namespace ActualChat;

/// <summary>
/// JS-interop snapshot of all client-relevant <see cref="Constants"/> sections.
/// Mirrored in TS as <c>AppConstants</c> in <c>app-constants.ts</c>; consumed
/// via the <c>APP_CONSTANTS</c> field (and per-section shortcuts like <c>VIDEO</c>, <c>AUDIO</c>).
/// </summary>
public sealed partial record AppConstants
{
    public static AppConstants Instance { get; } = new();

    public VideoConstants Video { get; init; } = new();
    public AudioConstants Audio { get; init; } = new();
}
