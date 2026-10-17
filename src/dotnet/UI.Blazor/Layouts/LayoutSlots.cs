namespace ActualChat.UI.Blazor.Layouts;

public static class LayoutSlots
{
    public const string MainLeftDrawer = nameof(MainLeftDrawer);
    public const string MainHeader = nameof(MainHeader);
    public const string SubHeader = nameof(SubHeader);
    public const string MainFooter = nameof(MainFooter);
    public const string SubFooter = nameof(SubFooter);
    public const string MainBodyOverlay = nameof(MainBodyOverlay);
    public const string MainRightDrawer = nameof(MainRightDrawer);
    // A nomad slot: for RenderIntoNomadSlot and RenderNomadSlot only
    public const string CallScreen = nameof(CallScreen);
    // The RenderNomadSlot key the full-screen call screen targets to dock as a layout column between
    // the left panel and the chat view, instead of covering the viewport. Never a chat id, so it can't
    // collide with the inline slot keyed by chat id.
    public const string CallScreenColumnKey = nameof(CallScreenColumnKey);
}
