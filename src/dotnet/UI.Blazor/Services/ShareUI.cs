namespace ActualChat.UI.Blazor.Services;

public class ShareUI
{
    private readonly ModalUI _modalUI;

    public ShareUI(ModalUI modalUI)
        => _modalUI = modalUI;

    public void ShareLink(string link, string title = "", string linkDescription = "")
        => ShareLink(new Uri(link), title, linkDescription);
    public void ShareLink(Uri link, string title = "", string linkDescription = "")
        => _ = _modalUI.Show(new ShareModalModel(link, linkDescription) { Title = title });
}
