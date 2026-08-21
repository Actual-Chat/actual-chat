namespace ActualChat.UI.Blazor;

/// <summary>
/// A component whose render <see cref="RenderGate"/> may hold back and replay later.
/// </summary>
public interface IPostponableRenderer
{
    void ResumeRender();
}
