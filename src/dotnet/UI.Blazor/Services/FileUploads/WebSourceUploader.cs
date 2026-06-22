using ActualChat.UI.Blazor.Module;
using ActualChat.UI.Services;

namespace ActualChat.UI.Blazor.Services;

public sealed class WebSourceUploader(IJSRuntime js) : IFileUploader
{
    private static readonly string JSStartMethod = $"{BlazorUICoreModule.ImportName}.StreamFileUpload.startWithReporter";

    public bool CanUpload(IUploadStreamSource source) => source is WebUploadStreamSource;

    public async Task Upload(
        IUploadStreamSource source,
        UploadId uploadId,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        var webSource = (WebUploadStreamSource)source;
        progress ??= new Progress<double>(_ => { });

        using var backend = new WebFileUploaderBackend(progress);
        var blazorRef = backend.BlazorRef;

        IJSObjectReference? fileUpload = null;
        try {
            fileUpload = await js.InvokeAsync<IJSObjectReference>(
                    JSStartMethod,
                    ct,
                    uploadId.Value,
                    webSource.JSRef, // IUploadStreamSource
                    blazorRef
                )
                .ConfigureAwait(false);
            ct.Register(() => _ = CancelUpload(fileUpload, CancellationToken.None));

            await backend.WhenUploadCompleted.WaitAsync(ct).ConfigureAwait(false);
        }
        finally {
            await fileUpload.DisposeSilentlyAsync().ConfigureAwait(false);
        }

        static async ValueTask CancelUpload(IJSObjectReference fileUpload, CancellationToken ct1)
        {
            try {
                await fileUpload.InvokeVoidAsync("cancel", ct1).ConfigureAwait(false);
            }
            catch {
                // Intended
            }
        }
    }
}
