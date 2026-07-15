namespace ActualChat.UI.Blazor.App.Components;

/// <summary>
/// One row of a <see cref="LastEntriesPreview"/>: an entry plus both places its text can live - its own
/// markup once persisted, or the transcript stream while it is still being spoken.
/// </summary>
public sealed record PreviewEntry(ChatEntry Entry, Markup Markup, string StreamingText = "");
