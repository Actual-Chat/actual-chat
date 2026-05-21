using ActualChat.Streaming;
using ActualChat.UI.Blazor.App.Services;
using Microsoft.AspNetCore.Components.Rendering;

namespace ActualChat.UI.Blazor.App.Components.VideoPanel;

public partial class VideoDiagnosticsModal
{
    private RenderFragment RenderOutboundQualityControl(ComputedModel m) => builder => {
        var qualityUi = Hub.VideoQualityUI;
        var bw = qualityUi.OutboundBandwidthEstimator;
        var encLayers = qualityUi.OutboundEncodingLayers;
        var bwLayers = qualityUi.OutboundBandwidthLayers;
        var activeKinds = m.OwnSourceKinds;
        var statsByKind = activeKinds
            .Select(k => (Kind: k, Stats: qualityUi.GetRecordingSnapshot(k).Health))
            .Where(x => x.Stats is not null)
            .ToDictionary(x => x.Kind, x => x.Stats!);

        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "diag-section");
        builder.AddMarkupContent(2, "<div class=\"diag-section-header\">Quality Control</div>");
        AppendVerdictChips(builder, 50,
            ("Encoder", qualityUi.OutboundEncoderHealth.Verdict),
            ("Uplink", qualityUi.OutboundUplinkHealth.Verdict));
        AppendCeilingAndSignal(builder, 100, bw);
        AppendOutboundInputs(builder, 200, activeKinds, statsByKind);
        AppendCapReadout(builder, 300, "Encoding cap", encLayers, activeKinds);
        AppendCapReadout(builder, 400, "Bandwidth cap", bwLayers, activeKinds);
        AppendHistory(builder, 500, bw);
        builder.CloseElement();
    };

    private static void AppendOutboundInputs(
        RenderTreeBuilder builder, int seqBase,
        IReadOnlyCollection<VideoSourceKind> activeKinds,
        IReadOnlyDictionary<VideoSourceKind, RecorderStats> statsByKind)
    {
        double fusedDropRatio = 0, fusedAckAgeMs = -1, fusedEncodeRatio = 0;
        foreach (var k in activeKinds) {
            if (!statsByKind.TryGetValue(k, out var stats)) continue;
            fusedDropRatio = Math.Max(fusedDropRatio, stats.SenderFrameDropRatioEma);
            if (stats.LastAckAgeMs >= 0)
                fusedAckAgeMs = Math.Max(fusedAckAgeMs, stats.LastAckAgeMs);
            fusedEncodeRatio = Math.Max(fusedEncodeRatio, stats.EncodeRatioEma);
        }
        AppendRow(builder, seqBase + 0, "Wire drop ratio", fusedDropRatio.ToString("F3"));
        AppendRow(builder, seqBase + 1, "Ack age",
            fusedAckAgeMs < 0 ? "n/a" : fusedAckAgeMs.ToString("F0") + " ms");
        AppendRow(builder, seqBase + 2, "Encode ratio", fusedEncodeRatio.ToString("F2"));
    }

    private static void AppendCapReadout(
        RenderTreeBuilder builder, int seqBase, string label,
        LayerCap cap,
        IReadOnlyCollection<VideoSourceKind> activeKinds)
    {
        var parts = new List<string>(2);
        if (activeKinds.Contains(VideoSourceKind.Camera))
            parts.Add($"camera {cap.CameraLayers}/{cap.DeviceCameraCap}");
        if (activeKinds.Contains(VideoSourceKind.ScreenCast))
            parts.Add($"screencast {cap.ScreencastLayers}/{cap.ScreencastCap}");
        AppendRow(builder, seqBase, label,
            parts.Count > 0 ? string.Join(", ", parts) : "n/a");
    }
}
