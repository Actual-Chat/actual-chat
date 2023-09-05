using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.Components;

internal class ModalStepRefImpl(HistoryStepRef stepRef, ModalStepRefImpl? parentStepRef) : ModalStepRef
{
    public override Symbol Id => stepRef.Id;

    public ModalStepRefImpl? ParentStepRef { get; } = parentStepRef;

    public HistoryStepRef RawStepRef => stepRef;

    public bool? IsModalClosing { get; set; }

    public void MarkClosed(bool isModalClosing)
        => MarkClosedInternal(isModalClosing);
}
