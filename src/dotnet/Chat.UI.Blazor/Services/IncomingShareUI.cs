using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public class IncomingShareUI
{
    private readonly ModalUI _modalUI;

    public IncomingShareUI(ModalUI modalUI)
        => _modalUI = modalUI;

    public void ShareText(string plainText)
        => _ = _modalUI.Show(new IncomingShareModal.Model(plainText));

    public void ShareFiles(string mimiType, IncomingShareFile[] files)
        => throw new NotImplementedException();
}
