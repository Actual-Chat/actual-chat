namespace ActualChat.UI.Blazor.Host;

public record NavbarLinkModel(
    string Name,
    string Href,
    NavbarLinkModelStatus Status = NavbarLinkModelStatus.None
);

public record NavbarLinksGroupModel(
    string Name,
    NavbarLinkModel[] Links
);

public enum NavbarLinkModelStatus : int
{
    None = 0,
    Online = 1,
    Offline = 2,
}
