using ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;

namespace ActualChat.App.Maui.IosShareExt.UI;

public class UIServiceBase(IosHub hub) : ProcessorBase
{
    protected IosHub Hub => hub;
    protected ILogger Log => field ??= Hub.Services.LogFor(GetType());
}
