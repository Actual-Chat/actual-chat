namespace ActualChat.UI.Blazor.Components;

public class DialogFrameNarrowViewSettingsBuilder
{
    public DialogFrameNarrowViewSettings GetFrom(
        IReadOnlyCollection<DialogButtonInfo>? buttonInfos,
        DialogFramePosition position)
        => position == DialogFramePosition.Bottom
            ? DialogFrameNarrowViewSettings.Bottom
            : DialogFrameNarrowViewSettings.Default;
}
