using ActualChat.UI.Blazor.App.Services;
using Microsoft.AspNetCore.Components.Rendering;

namespace ActualChat.UI.Blazor.App.Components.VideoPanel;

public partial class VideoDiagnosticsModal
{
    private RenderFragment RenderInboundQualityControl() => builder => {
        var bw = Hub.VideoQualityUI.InboundBandwidthEstimator;
        var snap = Hub.VideoQualityUI.PlaybackSnapshot;

        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "diag-section");
        builder.AddMarkupContent(2, "<div class=\"diag-section-header\">Quality Control</div>");
        AppendCeilingAndSignal(builder, 100, bw);
        AppendInboundInputs(builder, 200, snap);
        AppendHistory(builder, 300, bw);
        builder.CloseElement();
    };

    private static void AppendInboundInputs(
        RenderTreeBuilder builder, int seqBase,
        VideoQualityUI.PlaybackQualitySnapshot snap)
    {
        AppendRow(builder, seqBase + 0, "Allocation budget",
            (snap.EstimatedCapacityBytesPerSec * 8 / 1000).ToString("0") + " kbps");
        AppendRow(builder, seqBase + 1, "Playback rate", snap.PlaybackRateEma.ToString("F3"));
        AppendRow(builder, seqBase + 2, "Drop ratio", snap.DropRatio.ToString("F3"));
    }
}
