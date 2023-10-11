using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Services;

public class IncomingShareUI(ModalUI modalUI)
{
    public void ShareText(string plainText)
        => _ = modalUI.Show(new IncomingShareModal.Model(plainText));

    public void ShareFiles(IncomingShareFile[] files)
        => _ = modalUI.Show(new IncomingShareModal.Model(files));
}
