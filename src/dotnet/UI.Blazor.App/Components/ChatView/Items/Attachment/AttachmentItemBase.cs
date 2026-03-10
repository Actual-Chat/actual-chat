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
        var attachment = Attachment;
        var previewState = await AttachmentsState.GetPreview(attachment.Id, cancellationToken).ConfigureAwait(false);
        var uploadState = await AttachmentsState.GetProgress(attachment.Id, cancellationToken).ConfigureAwait(false);
        return new Model(attachment, previewState, uploadState);
    }

    // Nested types
    public record Model(Attachment Attachment, AttachmentPreview Preview, AttachmentProgress Progress)
    {
        public static readonly Model None = new(null!, AttachmentPreview.NoPreview, AttachmentProgress.New);

        public bool NoAccess => Preview.State == PreviewAccessState.NoFileAccess;

        public bool HasPreview => Attachment.IsSupportedImage
            || Attachment.IsSupportedVideo
            || HasCustomPreview;

        // Custom preview is a generated thumbnail (e.g., iOS MOV thumbnail) with content:// scheme
        private bool HasCustomPreview => Attachment is { Width: > 0, Height: > 0 }
            && Preview.PreviewUrl.OrdinalStartsWith($"{UrlMapper.UriContentScheme}://");
    }
}
