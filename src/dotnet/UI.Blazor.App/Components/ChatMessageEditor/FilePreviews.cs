using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public class FilePreviews(IJSRuntime js, ILogger<FilePreviews> log)
{
    private static readonly string JSGetDimensions = $"{BlazorUIAppModule.ImportName}.FilePreviews.getDimensions";

    public async Task<FilePreview?> Get(IFileProvider fileProvider, string fileType, CancellationToken cancellationToken = default)
    {
        if (!MediaTypeExt.IsVisualMedia(fileType))
            return null;

        var preview = await fileProvider.GetPreview(cancellationToken);
        if (preview.Dimensions != null)
            return preview;

        var previewFileType = MediaMimeTypes.TryGetMimeType(preview.Url, out var type) ? type : fileType;
        var size = await GetSize(preview.Url, previewFileType);
        return preview with { Dimensions = size };
    }

    private async Task<Size2D?> GetSize(string previewUrl, string fileType)
    {
        if (!MediaTypeExt.IsVisualMedia(fileType))
            return null;

        try {
            return await js.InvokeAsync<Size2D>(JSGetDimensions, previewUrl, fileType).ConfigureAwait(false);
        }
        catch (Exception e) {
            log.LogWarning(e, "Failed to get visual media dimensions: '{PreviewUrl}', '{FileType}'", previewUrl, fileType);
            return null;
        }
    }
}
