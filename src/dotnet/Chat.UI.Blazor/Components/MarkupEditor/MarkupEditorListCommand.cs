namespace ActualChat.Chat.UI.Blazor.Components;

public class MarkupEditorListCommand
{
    public MarkupEditorListCommandKind Kind { get; init; }
    public string? Filter { get; init; }
}

public enum MarkupEditorListCommandKind
{
    Show,
    Hide,
    GoToNextItem,
    GoToPreviousItem,
    InsertItem,
}
