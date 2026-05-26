using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Services;
using Microsoft.AspNetCore.Components.Rendering;

namespace ActualChat.UI.Blazor.App.Components.VideoPanel;

public partial class VideoDiagnosticsModal
{
    private RenderFragment RenderInboundQualityControl() => builder => {
        var qualityUi = Hub.VideoQualityUI;
        var bw = qualityUi.InboundBandwidthEstimator;
        var snap = qualityUi.PlaybackSnapshot;

        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "diag-section");
        builder.AddMarkupContent(2, "<div class=\"diag-section-header\">Quality Control</div>");
        AppendCeilingAndSignal(builder, 100, bw);
        AppendInboundInputs(builder, 200, snap, qualityUi.InboundDecoderCapStreamCount);
        AppendDecisionLog(builder, 300, qualityUi.InboundDecisionLog, "Dl", "Dec");
        builder.CloseElement();
    };

    private static void AppendInboundInputs(
        RenderTreeBuilder builder, int seqBase,
        VideoQualityUI.PlaybackQualitySnapshot snap,
        int decoderCapStreamCount)
    {
        AppendRow(builder, seqBase + 0, "Allocation budget",
            (snap.EstimatedCapacityBytesPerSec * 8 / 1000).ToString("0") + " kbps");
        AppendRow(builder, seqBase + 1, "Playback rate", snap.PlaybackRateEma.ToString("F3"));
        AppendRow(builder, seqBase + 2, "Drop ratio", snap.DropRatio.ToString("F3"));
        AppendRow(builder, seqBase + 3, "Decoder-capped streams", decoderCapStreamCount.ToString());
    }
}
