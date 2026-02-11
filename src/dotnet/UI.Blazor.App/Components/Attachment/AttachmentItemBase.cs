using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public abstract class AttachmentItemBase : ComputedStateComponent<AppUIHub, AttachmentItemBase.Model>
{
    private AttachmentsState AttachmentsState => Hub.AttachmentsState;

    [Parameter, EditorRequired] public Attachment Attachment { get; set; } = null!;
    [Parameter] public EventCallback RemoveClick { get; set; }
    [Parameter] public EventCallback RestartClick { get; set; }

    protected override ComputedState<Model>.Options GetStateOptions()
        => new() { InitialValue = Model.None };

    protected override async Task<Model> ComputeState(CancellationToken cancellationToken)
    {
        var id = Attachment.Id;
        var previewState = await AttachmentsState.GetPreview(id, cancellationToken).ConfigureAwait(false);
        var uploadState = await AttachmentsState.GetProgress(id, cancellationToken).ConfigureAwait(false);
        return new Model(previewState, uploadState);
    }

    // Nested types
    public record Model(AttachmentPreview Preview, AttachmentProgress Progress)
    {
        public static readonly Model None = new(AttachmentPreview.NoPreview, AttachmentProgress.New);

        public bool NoAccess => Preview.State == PreviewAccessState.NoFileAccess;
    }
}
