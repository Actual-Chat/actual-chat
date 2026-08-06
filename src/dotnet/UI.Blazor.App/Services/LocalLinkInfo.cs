
namespace ActualChat.UI.Blazor.App.Services;

public record LocalLinkInfo(LocalUrl LocalUrl)
{
    public virtual bool CanRender => false;
}

public record ChatLocalLinkInfo(LocalUrl LocalUrl, Chat.Chat Chat) : LocalLinkInfo(LocalUrl)
{
    public ChatEntry? Entry { get; init; }
    public Author? Author { get; init; }
    public Place? Place { get; init; }

    public override bool CanRender => true;
}

public record UserLocalLinkInfo(LocalUrl LocalUrl, Account Account) : LocalLinkInfo(LocalUrl)
{
    public Chat.Chat? Chat { get; init; }

    public override bool CanRender => true;
}

public record PlaceLocalLinkInfo(LocalUrl LocalUrl, Place Place) : LocalLinkInfo(LocalUrl)
{
    public override bool CanRender => true;
}
