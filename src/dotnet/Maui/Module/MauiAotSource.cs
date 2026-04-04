using ActualChat.Aot;
using ActualChat.Maui.Services;

namespace ActualChat.Maui.Module;

internal class MauiAotSource : IAotSource
{
    public void KeepTypes()
    {
        if (CodeKeeper.AlwaysTrue)
            return;

        CodeKeeper.Keep<SQLiteRemoteComputedCache>();
        CodeKeeper.Keep<SQLiteBatchingKvasBackend>();
        CodeKeeper.Keep("ActualChat.Maui.Services.SQLiteBatchingKvasBackend+DbHelpers, ActualChat.Maui");
    }

    public (Type, AotTypeKind)[] ListTypes() => [];
}
