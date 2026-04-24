namespace ActualChat.App.AotHelper;

/// <summary>
/// Walks the member graph of serializable root types and collects every concrete type T
/// that needs <c>MessagePackByteSerializer&lt;T&gt;</c> (plus <c>Func</c> / <c>Expression</c>)
/// retained for Native AOT.
/// <para>
/// ActualLab.Serialization constructs these serializers through an
/// <c>Expression&lt;Func&lt;MessagePackSerializerOptions, Type, MessagePackByteSerializer&lt;T&gt;&gt;&gt;</c>
/// factory compiled per T at runtime. Under NativeAOT, every concrete T that reaches this
/// factory must have its generic instantiation kept explicitly — otherwise ILC drops it.
/// </para>
/// <para>
/// Emits C# type expressions (e.g. <c>global::ActualChat.Users.Presence</c>,
/// <c>global::ActualLab.Api.ApiArray&lt;global::ActualChat.Live.LiveStreamInfo&gt;</c>)
/// suitable for <c>CodeKeeper.Keep&lt;T&gt;()</c> generic calls.
/// </para>
/// </summary>
public static class MessagePackByteSerializerDiscovery
{
    public static SortedSet<string> DiscoverAll(
        IEnumerable<Type> serializableRoots,
        IEnumerable<Type>? apiInterfaces = null)
    {
        var visited = new HashSet<Type>();
        var typeExprs = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var root in serializableRoots)
            Walk(root, visited, typeExprs);
        if (apiInterfaces != null) {
            foreach (var iface in apiInterfaces)
                WalkApiInterface(iface, visited, typeExprs);
        }
        return typeExprs;
    }

    private static void WalkApiInterface(Type iface, HashSet<Type> visited, SortedSet<string> typeExprs)
    {
        // API methods' parameter + return types (unwrapped from Task<T> / ValueTask<T>) can
        // reach enum / DTO types that never appear as DTO fields.
        foreach (var method in iface.GetMethods(BindingFlags.Public | BindingFlags.Instance)) {
            foreach (var p in method.GetParameters())
                Walk(p.ParameterType, visited, typeExprs);
            Walk(UnwrapAsync(method.ReturnType), visited, typeExprs);
        }
    }

    private static Type UnwrapAsync(Type type)
    {
        if (!type.IsGenericType)
            return type;
        var def = type.GetGenericTypeDefinition();
        if (def == typeof(Task<>) || def == typeof(ValueTask<>))
            return type.GetGenericArguments()[0];
        return type;
    }

    private static void Walk(Type type, HashSet<Type> visited, SortedSet<string> typeExprs)
    {
        if (!visited.Add(type))
            return;
        if (type.IsGenericTypeDefinition || type.IsGenericParameter
            || type.IsPointer || type.IsByRef
            || type == typeof(void) || type == typeof(object))
            return;

        if (ShouldEmit(type)) {
            var expr = FormatTypeExpr(type);
            if (expr != null)
                typeExprs.Add(expr);
        }

        // Always descend into generic args / array element / nullable underlying.
        if (type.IsArray) {
            Walk(type.GetElementType()!, visited, typeExprs);
        }
        else {
            var nullableUnderlying = Nullable.GetUnderlyingType(type);
            if (nullableUnderlying != null)
                Walk(nullableUnderlying, visited, typeExprs);

            if (type.IsGenericType) {
                foreach (var arg in type.GetGenericArguments())
                    Walk(arg, visited, typeExprs);
            }
        }

        // For BCL / external types, stop here — don't walk their internal members
        // (e.g. Dictionary.Keys / Values return KeyCollection / ValueCollection
        // which are not meant to be serialized).
        if (!IsOurAssembly(type))
            return;

        if (type.BaseType != null && type.BaseType != typeof(object) && type.BaseType != typeof(ValueType))
            Walk(type.BaseType, visited, typeExprs);

        // Walk generic arguments of implemented interfaces to pick up result types like Unit
        // from `ISessionCommand<Unit>`.
        foreach (var iface in type.GetInterfaces()) {
            if (!iface.IsGenericType)
                continue;
            foreach (var arg in iface.GetGenericArguments())
                Walk(arg, visited, typeExprs);
        }

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
            if (IsIgnored(prop))
                continue;
            Walk(prop.PropertyType, visited, typeExprs);
        }
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)) {
            if (IsIgnored(field))
                continue;
            Walk(field.FieldType, visited, typeExprs);
        }
    }

    private static bool IsOurAssembly(Type type)
    {
        var asm = type.Assembly.GetName().Name ?? "";
        return asm.StartsWith("ActualChat.", StringComparison.Ordinal)
            || asm.StartsWith("ActualLab.", StringComparison.Ordinal)
            || asm == "ActualChat" || asm == "ActualLab";
    }

    private static bool ShouldEmit(Type type)
    {
        // Primitives + universal BCL types are covered by the static Core list.
        if (type.IsPrimitive) return false;
        if (type == typeof(string)) return false;
        if (type == typeof(decimal) || type == typeof(DateTime) || type == typeof(DateTimeOffset)
            || type == typeof(TimeSpan) || type == typeof(Guid)
            || type == typeof(DateOnly) || type == typeof(TimeOnly))
            return false;
        // Framework value types already covered.
        if (type.FullName is "ActualLab.Text.Symbol" or "ActualLab.Time.Moment" or "ActualLab.Reflection.TypeRef")
            return false;
        // Ref structs can't be generic arguments.
        if (type.IsByRefLike) return false;
        // Skip types that can't be referenced via a named expression (anonymous, open generic).
        if (string.IsNullOrEmpty(type.FullName))
            return false;
        // Skip inaccessible (non-public) nested types — they can't appear in source keeps.
        if (!IsPubliclyAccessible(type))
            return false;
        return true;
    }

    private static bool IsPubliclyAccessible(Type type)
    {
        while (type.IsNested) {
            if (!type.IsNestedPublic)
                return false;
            type = type.DeclaringType!;
        }
        return type.IsPublic;
    }

    private static string? FormatTypeExpr(Type type)
    {
        if (type.IsArray) {
            var elem = FormatTypeExpr(type.GetElementType()!);
            return elem == null ? null : $"{elem}[]";
        }

        // Collect the path from outermost declaring type to this type.
        var segments = new List<Type>();
        var cur = type;
        while (cur != null) {
            segments.Insert(0, cur);
            cur = cur.IsNested ? cur.DeclaringType : null;
        }

        // All generic args (includes those inherited from outer nested types).
        var allArgs = type.IsGenericType ? type.GetGenericArguments() : [];

        var parts = new List<string>(segments.Count);
        var argIdx = 0;
        for (var i = 0; i < segments.Count; i++) {
            var seg = segments[i];
            var localArity = LocalGenericArity(seg);

            var simpleName = seg.Name;
            var tickIdx = simpleName.IndexOf('`');
            if (tickIdx > 0) simpleName = simpleName.Substring(0, tickIdx);

            var prefix = "";
            if (i == 0 && !string.IsNullOrEmpty(seg.Namespace))
                prefix = seg.Namespace + ".";

            string segPart;
            if (localArity == 0) {
                segPart = prefix + simpleName;
            }
            else {
                var localArgExprs = new string[localArity];
                for (var k = 0; k < localArity; k++) {
                    var argExpr = FormatTypeExpr(allArgs[argIdx + k]);
                    if (argExpr == null)
                        return null;
                    localArgExprs[k] = argExpr;
                }
                argIdx += localArity;
                segPart = prefix + simpleName + "<" + string.Join(", ", localArgExprs) + ">";
            }
            parts.Add(segPart);
        }

        return "global::" + string.Join(".", parts);
    }

    private static int LocalGenericArity(Type type)
    {
        // The backtick suffix in Type.Name reflects the type's own generic parameter count
        // (not the total including inherited ones).
        var name = type.Name;
        var tickIdx = name.IndexOf('`');
        if (tickIdx < 0) return 0;
        return int.Parse(name.AsSpan(tickIdx + 1), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool IsIgnored(MemberInfo member)
    {
        foreach (var a in member.CustomAttributes) {
            var n = a.AttributeType.FullName;
            if (n is "System.Runtime.Serialization.IgnoreDataMemberAttribute"
                or "MessagePack.IgnoreMemberAttribute"
                or "System.Text.Json.Serialization.JsonIgnoreAttribute")
                return true;
        }
        return false;
    }
}
