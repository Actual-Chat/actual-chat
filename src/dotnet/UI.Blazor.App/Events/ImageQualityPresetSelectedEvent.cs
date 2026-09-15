using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Events;

public sealed record ImageQualityPresetSelectedEvent(string SelectorId, ImageQualityPreset Preset) : IUIEvent;
