namespace ActualChat.UI.Blazor.Components.Internal;

public interface IModalRefImpl
{
    RenderFragment View { get; }

    void SetView(RenderFragment view);
    void SetModal(Modal modal);
    void MarkClosed();
    void CloseSteps();
}
