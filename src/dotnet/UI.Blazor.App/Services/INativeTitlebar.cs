namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// The titlebar of a native desktop host whose page runs under it (the AppKit app).
/// Registered only there.
/// </summary>
public interface INativeTitlebar
{
    void SetInsetWindowButtons(bool mustInset);
}
