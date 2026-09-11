using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

// No StateEqualityComparer: the leaves render their own parameters alongside the model, and a
// model-only comparer would swallow the render a parameter-only change needs.
public abstract class AttachmentItemBase : ComputedRenderStateComponent<AppUIHub, AttachmentItemBase.Model>
{
    private AttachmentsState AttachmentsState => Hub.AttachmentsState;

    [Parameter, EditorRequired] public Attachment Attachment { get; set; } = null!;
    [Parameter] public EventCallback RemoveClick { get; set; }
    [Parameter] public EventCallback RestartClick { get; set; }

    protected override ComputedState<Model>.Options GetStateOptions()
        => new() { InitialValue = new Model(Attachment, AttachmentPreview.NoPreview, AttachmentProgress.New) };

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
        public bool NoAccess => Preview.State == PreviewAccessState.NoFileAccess;

        // Custom preview is a generated thumbnail (e.g., iOS MOV thumbnail) with content:// scheme
        public bool HasCustomPreview => !Attachment.Size.IsEmpty
            && Preview.PreviewUrl.StartsWith($"{UrlMapper.UriContentScheme}://");

        // Preview is a server-generated image thumbnail (e.g., a video snapshot frame)
        public bool HasImagePreview => Preview.Preview?.IsImagePreview == true;
    }
}
