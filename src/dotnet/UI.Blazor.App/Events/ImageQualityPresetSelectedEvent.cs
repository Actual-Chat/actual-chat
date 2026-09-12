using ActualChat.UI.Blazor.App.Components;

namespace ActualChat.UI.Blazor.App.Events;

public sealed record ImageQualityPresetSelectedEvent(string SelectorId, ImageQualityPreset Preset) : IUIEvent;
