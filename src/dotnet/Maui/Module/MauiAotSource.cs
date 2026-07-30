using ActualChat.Aot;
using ActualChat.Kvas;
using ActualChat.UI.Caching;

namespace ActualChat.Maui.Module;

internal class MauiAotSource : IAotSource
{
    public void KeepTypes()
    {
        if (CodeKeeper.AlwaysTrue)
            return;

        CodeKeeper.Keep<KvasarRemoteComputedCache>();
        CodeKeeper.Keep<KvasarKvas>();
    }

    public (Type, AotTypeKind)[] ListTypes() => [];
}
