using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Services;

public class IncomingShareUI(ModalUI modalUI)
{
    public void ShareText(string plainText)
        => _ = modalUI.Show(new IncomingShareModal.Model(plainText));

    public void ShareFiles(IncomingShareFile[] files)
        => _ = modalUI.Show(new IncomingShareModal.Model(files));
}
