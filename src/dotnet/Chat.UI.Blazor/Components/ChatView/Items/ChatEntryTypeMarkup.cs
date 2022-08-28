namespace ActualChat.Chat.UI.Blazor.Components;

public record ChatEntryTypeMarkup(ChatEntryType Type) : Markup
{
    public override string Format()
        => Type == ChatEntryType.Audio ? "<audio>" : "<text>";
}
