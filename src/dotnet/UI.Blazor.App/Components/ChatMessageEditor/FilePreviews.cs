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
        var info = await GetMetadata(preview.Url, previewFileType);
        return info is { } i
            ? preview with { Dimensions = new Size2D(i.Width, i.Height), DurationMs = i.DurationMs }
            : preview;
    }

    private async Task<VisualMediaInfo?> GetMetadata(string previewUrl, string fileType)
    {
        if (!MediaTypeExt.IsVisualMedia(fileType))
            return null;

        try {
            return await js.InvokeAsync<VisualMediaInfo>(JSGetDimensions, previewUrl, fileType).ConfigureAwait(false);
        }
        catch (Exception e) {
            log.LogWarning(e, "Failed to get visual media metadata: '{PreviewUrl}', '{FileType}'", previewUrl, fileType);
            return null;
        }
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct VisualMediaInfo(int Width, int Height, long DurationMs);
}
