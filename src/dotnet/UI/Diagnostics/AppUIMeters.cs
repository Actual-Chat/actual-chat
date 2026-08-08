using System.Diagnostics.Metrics;

// ReSharper disable once CheckNamespace
namespace ActualChat.Diagnostics;

public static class AppUIMeters
{
    public static readonly Counter<long> MessageLocalizationFallbackCount =
        AppUIInstruments.Meter.CreateCounter<long>(
            "app.ui.localization.fallback.count",
            null,
            "UI messages localized via AI fallback rather than the string catalog");
}
