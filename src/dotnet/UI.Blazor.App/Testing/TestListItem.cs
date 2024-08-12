namespace ActualChat.UI.Blazor.App.Testing;

public record TestListItem(
    int Key,
    string Title,
    string Description = "",
    double FontSize = 1
) { }
