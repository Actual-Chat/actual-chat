using ActualChat.Reflection;
using ActualLab.Interception;
using ActualLab.Interception.Trimming;

namespace ActualChat.UI.Blazor.App;

/// <summary>
/// A code keeper that prevents the .NET trimmer from removing App-specific proxy types,
/// computed functions, and RPC call types.
/// </summary>
[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "CodeKeepers are used only to retain the code")]
[UnconditionalSuppressMessage("Trimming", "IL2111", Justification = "CodeKeepers are used only to retain the code")]
[UnconditionalSuppressMessage("Trimming", "IL3050", Justification = "CodeKeepers are used only to retain the code")]
public class AppProxyCodeKeeper : ProxyCodeKeeper.IExtension
{
    public void KeepProxy<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TBase,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TProxy>()
        where TBase : IRequiresAsyncProxy where TProxy : IProxy
    { }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void KeepMethodArgument<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TArg>(
        string name, int index)
    {
        if (CodeKeeper.AlwaysTrue)
            return;

        CodeKeeper.Keep<RecordTypeInfoFactory<TArg>>();
        CodeKeeper.Keep<RecordTypeInfo<TArg>>();
    }

    public void KeepMethodResult<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResult,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TUnwrapped>(string name)
    {
        if (CodeKeeper.AlwaysTrue)
            return;

        CodeKeeper.Keep<RecordTypeInfoFactory<TUnwrapped>>();
        CodeKeeper.Keep<RecordTypeInfo<TUnwrapped>>();
    }
}
