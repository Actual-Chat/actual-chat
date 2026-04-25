using ActualChat.Reflection;
using MessagePack.Formatters;

namespace ActualChat.Serialization.Internal;

/// <summary>
/// A dynamic MessagePack resolver that honors [MessagePackObject]/[DataContract] contracts
/// but ignores [Key(N)] - it always serializes members by name as a string-keyed map.
/// </summary>
/// <remarks>
/// Internally invokes MessagePack's own <c>DynamicObjectResolver.BuildFormatterHelper</c>
/// with <c>forceStringKey: true, contractless: false</c> via reflection, since that flag combo
/// is not exposed as a public resolver. Relies on System.Reflection.Emit; server-only (not AOT-safe).
/// </remarks>
[UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Server-only; serializable types are preserved by callers.")]
[UnconditionalSuppressMessage("Trimming", "IL2060", Justification = "Server-only; serializable types are preserved by callers.")]
[UnconditionalSuppressMessage("Trimming", "IL2077", Justification = "Server-only; MessagePack internal types are preserved.")]
[UnconditionalSuppressMessage("Trimming", "IL2090", Justification = "Server-only; serializable types are preserved by callers.")]
[UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Server-only; not used on AOT platforms.")]
public sealed class DynamicForceStringKeyResolver : IFormatterResolver
{
    public static readonly DynamicForceStringKeyResolver Instance = new();

    private static readonly Assembly MessagePackAssembly = typeof(MessagePackSerializer).Assembly;

    private static readonly Type DynamicAssemblyFactoryType =
        MessagePackAssembly.GetType("MessagePack.Internal.DynamicAssemblyFactory", throwOnError: true)!;

    private static readonly object DynamicAssemblyFactory =
        Activator.CreateInstance(
            DynamicAssemblyFactoryType,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            args: ["ActualChat.Serialization.DynamicForceStringKeyResolver"],
            culture: null)
        ?? throw new InvalidOperationException(
            "Failed to construct MessagePack.Internal.DynamicAssemblyFactory - MessagePack package version mismatch?");

    private static readonly MethodInfo BuildFormatterHelperGenericMethod =
        typeof(MessagePack.Resolvers.DynamicObjectResolver)
            .GetMethod("BuildFormatterHelper", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            "MessagePack.Resolvers.DynamicObjectResolver.BuildFormatterHelper not found - MessagePack package version mismatch?");

    private DynamicForceStringKeyResolver() { }

    public IMessagePackFormatter<T>? GetFormatter<T>()
        => FormatterCache<T>.Formatter;

    // Nested types

    private static class FormatterCache<T>
    {
        public static readonly IMessagePackFormatter<T>? Formatter = GetFormatter();

        [UnconditionalSuppressMessage("Trimming", "IL2090", Justification = "Serializable types must be preserved by callers.")]
        [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "Server-only resolver; not used on AOT platforms.")]
        private static IMessagePackFormatter<T>? GetFormatter()
        {
            var type = typeof(T);
            if (type.Assembly.GetKind() != AssemblyKind.App)
                return null; // We don't alter types from non-app assemblies

            var attrs = type.GetCustomAttributes();
            // Require [MessagePackObject/Union/DataContract] attribute; otherwise let the next resolver handle it
            if (!attrs.Any(a => a is MessagePackObjectAttribute or UnionAttribute or DataContractAttribute))
                return null;

            var method = BuildFormatterHelperGenericMethod.MakeGenericMethod(type);
            return (IMessagePackFormatter<T>?)method.Invoke(null, [
                Instance,
                DynamicAssemblyFactory,
                /* forceStringKey */ true,
                /* contractless   */ false,
                /* allowPrivate   */ false,
            ]);
        }
    }
}
