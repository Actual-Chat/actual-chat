namespace ActualChat.Aot;

/// <summary>
/// Base interface for generated AOT "type sources" / code keepers.
/// Must be internal partial, if generated, and implement interface methods listed below
/// The only instance of each AOT source must be registered in <see cref="AotTypes"/> in module initializer,
/// see `ApiContractsModuleInitializer` for example.
/// </summary>
public interface IAotSource
{
    /// <summary>
    /// Calls <see cref="CodeKeeper.Keep{T}()"/> for every type registered via <see cref="ListTypes"/>.
    /// Must use `global::` prefix to reference `T`.
    /// </summary>
    void KeepTypes();

    /// <summary>
    /// Lists registered types.
    /// Must use `global::` prefix to reference `T`.
    /// </summary>
    (Type, AotTypeKind)[] ListTypes();
}
