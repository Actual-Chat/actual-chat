namespace ActualChat.Chat.UI.Blazor.Components;

public record ChatEntryKindMarker(ChatEntryKind Kind) : Markup
{
    public override string Format()
        => Kind == ChatEntryKind.Audio ? "<audio>" : "<text>";
}
