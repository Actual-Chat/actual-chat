namespace ActualChat.Chat.UI.Blazor.Components;

public record ChatEntryTypeMarkup(ChatEntryType Type) : CustomMarkup
{
    public override string ToMarkupText()
        => Type == ChatEntryType.Audio ? "<audio>" : "<text>";
}
