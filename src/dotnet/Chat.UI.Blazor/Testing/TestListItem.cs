namespace ActualChat.Chat.UI.Blazor.Testing;

public record TestListItem(
    int Key,
    string Title,
    string Description = "",
    double FontSize = 1
) { }
