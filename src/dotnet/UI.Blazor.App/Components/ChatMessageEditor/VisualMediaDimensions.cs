using ActualChat.Media;
using ActualChat.UI.Blazor.App.Module;

namespace ActualChat.UI.Blazor.App.Components;

public class VisualMediaDimensions(IJSRuntime js, ILogger<VisualMediaDimensions> log)
{
    private static readonly string JSGetDimensions = $"{BlazorUIAppModule.ImportName}.VisualMediaDimensions.getDimensions";

    public async Task<(int width, int height)> GetVisualMediaDimensions(string previewUrl, string fileType) {
        if (!MediaTypeExt.IsVisualMedia(fileType))
            return (0, 0);

        Dimensions? dimensions = null;
        try {
            dimensions = await js.InvokeAsync<Dimensions?>(JSGetDimensions, previewUrl, fileType).ConfigureAwait(false);
        }
        catch (Exception e) {
            log.LogWarning(e, "Failed to get visual media dimensions: '{PreviewUrl}', '{FileType}'", previewUrl, fileType);
        }
        return dimensions is null ? (0, 0) : (dimensions.Width, dimensions.Height);
    }

    // ReSharper disable once ClassNeverInstantiated.Local
    private class Dimensions
    {
        public int Width { get; set; }
        public int Height { get; set; }
    }
}
