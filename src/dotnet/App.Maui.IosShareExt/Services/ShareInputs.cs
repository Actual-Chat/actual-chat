using ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;
using ActualChat.Maui;

namespace ActualChat.App.Maui.IosShareExt.Services;

public class ShareInputs(IosHub hub)
{
    private ILogger Log => field ??= hub.LogFor(GetType());

    public Task<string> GetText(CancellationToken cancellationToken = default)
        => MauiMainThread.DispatchToMainThread(async () => {
            var inputs = await ListTextInputsUnsafe(cancellationToken).ConfigureAwait(false);
            return string.Join('\n', inputs);
        });

    public Task<List<NSItemProvider>> ListFiles(CancellationToken cancellationToken = default)
        => MauiMainThread.DispatchToMainThread(() => ListFileInputsUnsafe().ToList());

    public Task<bool> HasFiles()
        => MauiMainThread.DispatchToMainThread(() => ListFileInputsUnsafe().Any());

    private Task<string[]> ListTextInputsUnsafe(CancellationToken cancellationToken)
    {
        var inputItems = UIKitExt.ExtensionContext.InputItems;
        return inputItems.SelectMany(x => x.Attachments ?? [])
            .Where(x => x.HasText())
            .Select(x => x.GetText())
            .Collect(cancellationToken);
    }

    private IEnumerable<NSItemProvider> ListFileInputsUnsafe()
    {
        foreach (var input in UIKitExt.ExtensionContext.InputItems) {
            var items = input.Attachments?.Where(x => !x.HasText()) ?? [];
            foreach (var item in items)
                yield return item;
        }
    }
}
