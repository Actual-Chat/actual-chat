using ActualLab.Serialization.Internal;

namespace ActualChat.App.AotHelper;

/// <summary>
/// Discovers concrete MessagePack formatter types required by a set of serializable root types.
/// Runs at generation time (JIT) where MessagePack's resolvers can be fully exercised.
/// Emits <see cref="Type.AssemblyQualifiedName"/> strings suitable for <c>CodeKeeper.Keep(string)</c>.
/// </summary>
public static class MessagePackFormatterDiscovery
{
    // Formatter types whose assembly we're willing to keep by AQN.
    // Runtime-emitted formatters (DynamicObjectResolver+ etc.) live in dynamic assemblies and must be excluded.
    private static readonly HashSet<string> AllowedAssemblyPrefixes = new(StringComparer.Ordinal) {
        "MessagePack",
        "ActualLab.",
        "ActualChat.",
    };

    private static readonly MethodInfo GetFormatterMethod = typeof(IFormatterResolver)
        .GetMethod(nameof(IFormatterResolver.GetFormatter))!;

    /// <summary>
    /// Walks every reachable member type starting from <paramref name="rootTypes"/> and returns
    /// AQNs of all discovered MessagePack formatter types.
    /// </summary>
    public static SortedSet<string> DiscoverAll(IEnumerable<Type> rootTypes)
    {
        var visited = new HashSet<Type>();
        var formatterAqns = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var type in rootTypes)
            Walk(type, visited, formatterAqns);
        return formatterAqns;
    }

    private static void Walk(Type type, HashSet<Type> visited, SortedSet<string> formatterAqns)
    {
        if (!ShouldWalk(type))
            return;
        if (!visited.Add(type))
            return;

        // 1. Explicit [MessagePackFormatter(typeof(X))] attribute — captures custom formatters
        //    for identifier types, wrappers (ApiArray, Option, etc.), and any other marked type.
        foreach (var attrData in type.CustomAttributes) {
            if (attrData.AttributeType.FullName != "MessagePack.MessagePackFormatterAttribute")
                continue;
            if (attrData.ConstructorArguments.Count == 0)
                continue;
            if (attrData.ConstructorArguments[0].Value is not Type formatterType)
                continue;
            AddFormatterType(formatterType, formatterAqns);

            // Custom formatter is generic (e.g., StringIdentifierMessagePackFormatter<>)?
            // Close it over the type itself and keep the closed generic — that's the actual
            // type ILC needs native code for.
            if (formatterType.IsGenericTypeDefinition
                && formatterType.GetGenericArguments().Length == 1) {
                try {
                    var closed = formatterType.MakeGenericType(type);
                    AddFormatterType(closed, formatterAqns);
                } catch {
                    // Constraints may prevent closing; ignore.
                }
            }
        }

        // 2. Resolve the runtime formatter via the chain used at runtime.
        var formatter = TryResolveFormatter(type);
        if (formatter is not null)
            AddFormatterType(formatter.GetType(), formatterAqns);

        // 3. Recurse into reachable types.
        if (type.IsArray) {
            Walk(type.GetElementType()!, visited, formatterAqns);
        }
        else {
            var nullableUnderlying = Nullable.GetUnderlyingType(type);
            if (nullableUnderlying != null)
                Walk(nullableUnderlying, visited, formatterAqns);

            if (type.IsGenericType) {
                foreach (var arg in type.GetGenericArguments())
                    Walk(arg, visited, formatterAqns);
            }
        }

        // 4. Walk base type + interfaces. Command/result types are typically declared as
        //    `record Foo(...) : ISessionCommand<TResult>`, where TResult (e.g. Unit) is only
        //    reachable via the interface's generic arguments. Member walks miss those.
        if (type.BaseType != null)
            Walk(type.BaseType, visited, formatterAqns);
        foreach (var iface in type.GetInterfaces()) {
            if (!iface.IsGenericType)
                continue;
            foreach (var arg in iface.GetGenericArguments())
                Walk(arg, visited, formatterAqns);
        }

        // 5. Walk serialized members (properties + fields).
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
            if (IsIgnored(prop))
                continue;
            Walk(prop.PropertyType, visited, formatterAqns);
        }
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)) {
            if (IsIgnored(field))
                continue;
            Walk(field.FieldType, visited, formatterAqns);
        }
    }

    private static bool ShouldWalk(Type type)
    {
        if (type == typeof(void) || type == typeof(object) || type == typeof(string))
            return false;
        if (type.IsPrimitive)
            return false;
        if (type.IsPointer || type.IsByRef)
            return false;
        if (type.IsGenericParameter || type.IsGenericTypeDefinition)
            return false;
        if (type == typeof(decimal) || type == typeof(DateTime) || type == typeof(DateTimeOffset)
            || type == typeof(TimeSpan) || type == typeof(Guid)
            || type == typeof(DateOnly) || type == typeof(TimeOnly))
            return false;
        return true;
    }

    private static bool IsIgnored(MemberInfo member)
    {
        foreach (var a in member.CustomAttributes) {
            var n = a.AttributeType.FullName;
            if (n is "System.Runtime.Serialization.IgnoreDataMemberAttribute"
                or "MemoryPack.MemoryPackIgnoreAttribute"
                or "MessagePack.IgnoreMemberAttribute"
                or "System.Text.Json.Serialization.JsonIgnoreAttribute")
                return true;
        }
        return false;
    }

    private static object? TryResolveFormatter(Type type)
    {
        try {
            var typedMethod = GetFormatterMethod.MakeGenericMethod(type);
            return typedMethod.Invoke(DefaultMessagePackResolver.Instance, null);
        }
        catch {
            return null;
        }
    }

    private static void AddFormatterType(Type formatterType, SortedSet<string> formatterAqns)
    {
        if (formatterType.IsGenericTypeDefinition)
            return;
        var asmName = formatterType.Assembly.GetName().Name ?? "";
        if (!IsAllowedAssembly(asmName))
            return;
        var aqn = formatterType.AssemblyQualifiedName;
        if (aqn != null)
            formatterAqns.Add(aqn);
    }

    private static bool IsAllowedAssembly(string asmName)
    {
        foreach (var prefix in AllowedAssemblyPrefixes) {
            if (asmName == prefix.TrimEnd('.')
                || asmName.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
