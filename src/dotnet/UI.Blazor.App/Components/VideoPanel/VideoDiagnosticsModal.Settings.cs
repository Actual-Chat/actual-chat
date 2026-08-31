using ActualChat.UI.Blazor.App.Module;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components.VideoPanel;

public partial class VideoDiagnosticsModal
{
    private static readonly string JSGetSettingsMethod = $"{BlazorUIAppModule.ImportName}.getVideoDebugSettings";
    private static readonly string JSSetForceDecodeCodecMethod =
        $"{BlazorUIAppModule.ImportName}.setVideoDebugForceDecodeCodec";
    private static readonly string JSSetPreferredEncodeCodecMethod =
        $"{BlazorUIAppModule.ImportName}.setVideoDebugPreferredEncodeCodec";
    private static readonly string JSSetDownscalerModeMethod = $"{BlazorUIAppModule.ImportName}.setVideoDebugDownscalerMode";
    private static readonly string JSSetMaxOutboundLayerCountMethod =
        $"{BlazorUIAppModule.ImportName}.setVideoDebugMaxOutboundLayerCount";
    private static readonly string JSSetMaxInboundLayerCountMethod =
        $"{BlazorUIAppModule.ImportName}.setVideoDebugMaxInboundLayerCount";
    private static readonly string JSSetEstBandwidthMultiplierMethod =
        $"{BlazorUIAppModule.ImportName}.setVideoDebugEstBandwidthMultiplier";
    private static readonly string JSSetCaptureFpsOverrideMethod =
        $"{BlazorUIAppModule.ImportName}.setVideoDebugCaptureFpsOverride";
    private static readonly string JSCollectStreamHintsMethod =
        $"{BlazorUIAppModule.ImportName}.collectActiveStreamHints";
    private static readonly string JSGetShowFpsOverlayMethod =
        $"{BlazorUIAppModule.ImportName}.getShowFpsOverlay";
    private static readonly string JSSetShowFpsOverlayMethod =
        $"{BlazorUIAppModule.ImportName}.setShowFpsOverlay";
    private static readonly string JSGetShowCodecOverlayMethod =
        $"{BlazorUIAppModule.ImportName}.getShowCodecOverlay";
    private static readonly string JSSetShowCodecOverlayMethod =
        $"{BlazorUIAppModule.ImportName}.setShowCodecOverlay";

    private static readonly double[] BandwidthMultiplierOptions =
        [0.1, 0.25, 0.5, 0.75, 1.0, 1.25, 1.5];
    private static readonly int[] CaptureFpsOptions = [30, 24, 15, 10, 5];

    private string _forceDecodeCodec = "";
    private string _preferredEncodeCodec = "";
    private string _downscalerMode = "metadata";
    private int? _captureFpsOverride;
    private bool _showFpsOverlay;
    private bool _showCodecOverlay;
    private int? _maxOutboundLayerCount;
    private int? _maxInboundLayerCount;
    private double _estBandwidthMultiplier = 1.0;
    private bool _settingsLoaded;

    private async Task LoadSettings()
    {
        if (_settingsLoaded)
            return;
        _settingsLoaded = true;
        try {
            var settings = await Hub.JS.InvokeAsync<VideoDebugSettings>(JSGetSettingsMethod);
            _forceDecodeCodec = NormalizeCodec(settings.ForceDecodeCodec);
            _preferredEncodeCodec = NormalizeCodec(settings.PreferredEncodeCodec);
            _downscalerMode = settings.DownscalerMode ?? "metadata";
            _captureFpsOverride = settings.CaptureFpsOverride;
            _maxOutboundLayerCount = settings.MaxOutboundLayerCount;
            _maxInboundLayerCount = settings.MaxInboundLayerCount;
            _estBandwidthMultiplier = NormalizeMultiplier(settings.EstBandwidthMultiplier);
            _showFpsOverlay = await Hub.JS.InvokeAsync<bool>(JSGetShowFpsOverlayMethod);
            _showCodecOverlay = await Hub.JS.InvokeAsync<bool>(JSGetShowCodecOverlayMethod);
            await Hub.VideoQualityUI.SetDebugMaxRecordingLayerCount(_maxOutboundLayerCount, CancellationToken.None);
            await Hub.VideoQualityUI.SetDebugMaxPlaybackLayerCount(
                _maxInboundLayerCount,
                await CollectStreamHints(),
                CancellationToken.None);
            Hub.VideoQualityUI.SetDebugBandwidthMultiplier(_estBandwidthMultiplier);
            StateHasChanged();
        }
        catch (Exception e) {
            Log.LogWarning(e, "Load video diagnostics settings failed");
        }
    }

    // "" means no override; anything unrecognised is treated the same way.
    private static string NormalizeCodec(string? codec)
        => codec is "h264" or "hevc" or "vp9" or "av1" ? codec : "";

    private async Task OnForceDecodeCodecChange(ChangeEventArgs e)
    {
        _forceDecodeCodec = NormalizeCodec(e.Value?.ToString());
        StateHasChanged();
        await Hub.JS.InvokeVoidAsync(JSSetForceDecodeCodecMethod, _forceDecodeCodec);
    }

    private async Task OnPreferredEncodeCodecChange(ChangeEventArgs e)
    {
        _preferredEncodeCodec = NormalizeCodec(e.Value?.ToString());
        StateHasChanged();
        await Hub.JS.InvokeVoidAsync(JSSetPreferredEncodeCodecMethod, _preferredEncodeCodec);
    }

    private async Task OnDownscalerModeChange(ChangeEventArgs e)
    {
        var mode = e.Value?.ToString();
        _downscalerMode = mode is "webgl" or "canvas" or "metadata" or "webgpu" ? mode : "metadata";
        StateHasChanged();
        await Hub.JS.InvokeVoidAsync(JSSetDownscalerModeMethod, _downscalerMode);
    }

    private async Task OnShowFpsOverlayClick()
    {
        _showFpsOverlay = !_showFpsOverlay;
        StateHasChanged();
        await Hub.JS.InvokeVoidAsync(JSSetShowFpsOverlayMethod, _showFpsOverlay);
    }

    private async Task OnShowCodecOverlayClick()
    {
        _showCodecOverlay = !_showCodecOverlay;
        StateHasChanged();
        await Hub.JS.InvokeVoidAsync(JSSetShowCodecOverlayMethod, _showCodecOverlay);
    }

    private async Task OnMaxOutboundLayerCountChange(ChangeEventArgs e)
    {
        _maxOutboundLayerCount = ParseLayerCount(e.Value?.ToString());
        await Hub.JS.InvokeVoidAsync(JSSetMaxOutboundLayerCountMethod, _maxOutboundLayerCount);
        await Hub.VideoQualityUI.SetDebugMaxRecordingLayerCount(_maxOutboundLayerCount, CancellationToken.None);
    }

    private async Task OnMaxInboundLayerCountChange(ChangeEventArgs e)
    {
        _maxInboundLayerCount = ParseLayerCount(e.Value?.ToString());
        await Hub.JS.InvokeVoidAsync(JSSetMaxInboundLayerCountMethod, _maxInboundLayerCount);
        await Hub.VideoQualityUI.SetDebugMaxPlaybackLayerCount(
            _maxInboundLayerCount,
            await CollectStreamHints(),
            CancellationToken.None);
    }

    private async Task OnEstBandwidthMultiplierChange(ChangeEventArgs e)
    {
        _estBandwidthMultiplier = NormalizeMultiplier(ParseMultiplier(e.Value?.ToString()));
        await Hub.JS.InvokeVoidAsync(JSSetEstBandwidthMultiplierMethod, _estBandwidthMultiplier);
        Hub.VideoQualityUI.SetDebugBandwidthMultiplier(_estBandwidthMultiplier);
    }

    private async Task OnCaptureFpsOverrideChange(ChangeEventArgs e)
    {
        _captureFpsOverride = int.TryParse(e.Value?.ToString(), out var fps) && CaptureFpsOptions.Contains(fps)
            ? fps
            : null;
        StateHasChanged();
        await Hub.JS.InvokeVoidAsync(JSSetCaptureFpsOverrideMethod, _captureFpsOverride).ConfigureAwait(false);
    }

    private static string FormatLayerCount(int? layerCount)
        => layerCount?.ToString() ?? "";

    private static int? ParseLayerCount(string? value)
        => int.TryParse(value, out var layerCount) && layerCount is >= 1 and <= 3
            ? layerCount
            : null;

    private static string FormatBandwidthMultiplier(double value)
        => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static double ParseMultiplier(string? value)
        => double.TryParse(
                value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var v)
            ? v
            : 1.0;

    private static double NormalizeMultiplier(double value)
    {
        // Snap to closest option so localStorage can't push out-of-range values
        // into the controller.
        var closest = BandwidthMultiplierOptions[0];
        var bestDelta = Math.Abs(closest - value);
        for (var i = 1; i < BandwidthMultiplierOptions.Length; i++) {
            var d = Math.Abs(BandwidthMultiplierOptions[i] - value);
            if (d < bestDelta) {
                bestDelta = d;
                closest = BandwidthMultiplierOptions[i];
            }
        }
        return closest;
    }

    private async Task<IReadOnlyList<VideoQualityUI.PlaybackStreamHint>> CollectStreamHints()
    {
        var raw = await Hub.JS.InvokeAsync<JsHint[]>(JSCollectStreamHintsMethod);
        var hints = new List<VideoQualityUI.PlaybackStreamHint>(raw.Length);
        foreach (var h in raw)
            hints.Add(new VideoQualityUI.PlaybackStreamHint(h.streamId, h.currentLayerId));
        return hints;
    }

    private sealed record JsHint(string streamId, int currentLayerId);

    public sealed record VideoDebugSettings(
        string? ForceDecodeCodec,
        string? PreferredEncodeCodec,
        int? MaxOutboundLayerCount,
        int? MaxInboundLayerCount,
        double EstBandwidthMultiplier,
        string? DownscalerMode,
        int? CaptureFpsOverride);
}
