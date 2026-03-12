using ActualChat.Reflection;
using ActualLab.Fusion.Trimming;

namespace ActualChat.UI.Blazor.App;

/// <summary>
/// A code keeper that prevents the .NET trimmer from removing App-specific proxy types,
/// computed functions, and RPC call types.
/// </summary>
[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "CodeKeepers are used only to retain the code")]
[UnconditionalSuppressMessage("Trimming", "IL2111", Justification = "CodeKeepers are used only to retain the code")]
[UnconditionalSuppressMessage("Trimming", "IL3050", Justification = "CodeKeepers are used only to retain the code")]
public class AppProxyCodeKeeper : FusionProxyCodeKeeper
{
    public override void KeepMethodArgument<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TArg>(
        string name = "", int index = -1)
    {
        if (AlwaysTrue)
            return;

        RecordTypeInfo.KeepCodeForType<TArg>();
        base.KeepMethodArgument<TArg>(name, index);
    }

    public override void KeepMethodResult<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResult,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TUnwrapped>(string name = "")
    {
        if (AlwaysTrue)
            return;

        RecordTypeInfo.KeepCodeForType<TUnwrapped>();
        base.KeepMethodResult<TResult, TUnwrapped>(name);
    }
}
