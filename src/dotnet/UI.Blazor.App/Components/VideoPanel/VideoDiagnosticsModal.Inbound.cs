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
        // Worst decoder verdict across active streams — the chip should fire
        // as soon as any one stream's decoder is bad.
        var worstDecoder = HealthVerdict.Unknown;
        foreach (var (_, h) in qualityUi.InboundDecoderHealthByStream) {
            if (h.Verdict == HealthVerdict.Unknown) continue;
            if (worstDecoder == HealthVerdict.Unknown || (int)h.Verdict > (int)worstDecoder)
                worstDecoder = h.Verdict;
        }

        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "diag-section");
        builder.AddMarkupContent(2, "<div class=\"diag-section-header\">Quality Control</div>");
        AppendVerdictChips(builder, 50,
            ("Downlink", qualityUi.AggregateDownlinkVerdict),
            ("Decoder", worstDecoder));
        AppendCeilingAndSignal(builder, 100, bw);
        AppendInboundInputs(builder, 200, snap, qualityUi.InboundDecoderCapStreamCount);
        AppendHistory(builder, 300, bw);
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
