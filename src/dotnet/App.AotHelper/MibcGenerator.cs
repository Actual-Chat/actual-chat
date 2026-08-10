using ActualChat.Aot;
using static System.Console;

namespace ActualChat.App.AotHelper;

/// <summary>
/// Emits <c>aothelper.mibc</c>: every method of every type the CodeKeeper set names, plus the
/// instantiations ActualLab's proxy keepers and the async machinery create reflectively.
/// <para>
/// This is the static counterpart to the device-recorded profile. A recording only covers what the
/// session happened to run, and goes stale as the code moves; this covers the whole keeper set on
/// every build. Both are passed to crossgen2 — see docs/ios-specific.md.
/// </para>
/// </summary>
public static class MibcGenerator
{
    private const BindingFlags MemberFlags = BindingFlags.Public | BindingFlags.NonPublic
        | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static int Generate(string outputPath)
    {
        AotTypeTester.EnsureAssembliesLoaded();

        var unresolved = new List<string>();
        var methods = new List<MethodBase>();
        var types = CollectTypes();
        WriteLine($"Keeper types                : {types.Count}");

        ProxyKeepDiscovery.Discover(types, methods, unresolved);
        WriteLine($"+ proxy keeps               : {types.Count} types, {methods.Count} methods");

        // Built here so it sees every instantiation named so far: those fix the arguments the open
        // types below get instantiated with.
        var pool = GenericArgumentPool.Create(types);

        AsyncStateMachineKeepDiscovery.Discover(types, methods, unresolved, pool);
        WriteLine($"+ async state machines      : {types.Count} types, {methods.Count} methods");

        AddAncestors(types);
        WriteLine($"+ ancestors                 : {types.Count} types");

        AddNestedTypes(types);
        WriteLine($"+ nested types              : {types.Count} types");

        var builder = new MibcBuilder(Path.GetFileName(outputPath));
        var skipped = 0;
        foreach (var type in types.OrderBy(x => x.FullName, StringComparer.Ordinal)) {
            foreach (var method in GetMethods(type)) {
                if (CanEmit(method))
                    builder.Add(method);
                else
                    skipped++;
            }
        }
        foreach (var method in methods) {
            if (CanEmit(method))
                builder.Add(method);
            else
                skipped++;
        }

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        builder.Save(outputPath);

        if (unresolved.Count > 0) {
            Error.WriteLine($"WARNING: {unresolved.Count} keeper name(s) did not resolve - "
                + "ActualLab may have renamed them:");
            foreach (var name in unresolved.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
                Error.WriteLine($"  {name}");
        }
        WriteLine($"Emitted {builder.MethodCount} methods in {builder.GroupCount} groups ({skipped} skipped)");
        WriteLine($"Generated: {outputPath} ({new FileInfo(outputPath).Length:N0} bytes)");
        return 0;
    }

    private static HashSet<Type> CollectTypes()
    {
        var types = new HashSet<Type>();
        var serializables = new List<Type>();
        foreach (var (type, kind) in AotTypes.All) {
            if (!Add(types, type))
                continue;
            if (kind == AotTypeKind.Serializable)
                serializables.Add(type);
        }

        foreach (var aqn in StjConverterDiscovery.DiscoverAll())
            AddByName(types, aqn);
        foreach (var aqn in MessagePackFormatterDiscovery.DiscoverAll(serializables, []))
            AddByName(types, aqn);

        // Constructed by an Expression<Func<...>> factory per T, so nothing references these
        // instantiations from IL - the same reason they end up interpreted on iOS.
        if (FindType("ActualLab.Serialization.MessagePackByteSerializer`1") is { } serializerDefinition)
            foreach (var type in serializables)
                TryAddInstantiation(types, serializerDefinition, type);

        return types;
    }

    // GetMethods below uses DeclaredOnly, so a type contributes nothing its base declares. Pulling the
    // whole chain in keeps base methods at their own declaring type, which is also what names the
    // correct instantiation for a generic base such as ComputedStateComponent<THub, TModel>.
    private static void AddAncestors(HashSet<Type> types)
    {
        foreach (var type in types.ToList()) {
            for (var ancestor = type.BaseType; ancestor != null; ancestor = ancestor.BaseType) {
                if (!Add(types, ancestor))
                    break;
            }
        }
    }

    // Lambdas and local functions compile into a nested <>c / <>c__DisplayClass, and a generic type's
    // nested types are reported open even when the parent is constructed - so they need re-instantiating
    // with the parent's arguments.
    private static void AddNestedTypes(HashSet<Type> types)
    {
        foreach (var type in types.ToList()) {
            Type[] nested;
            try {
                nested = type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);
            }
            catch (Exception e) when (e is TypeLoadException or FileNotFoundException) {
                continue;
            }
            foreach (var nestedType in nested) {
                if (!nestedType.ContainsGenericParameters) {
                    types.Add(nestedType);
                    continue;
                }
                if (!type.IsConstructedGenericType)
                    continue;

                var arguments = type.GetGenericArguments();
                if (nestedType.GetGenericArguments().Length != arguments.Length)
                    continue;
                try {
                    types.Add(nestedType.MakeGenericType(arguments));
                }
                catch (Exception e) when (e is ArgumentException or TypeLoadException) {
                    // Constraint violation - there is no such instantiation to keep.
                }
            }
        }
    }

    private static bool Add(HashSet<Type> types, Type type)
        => !type.ContainsGenericParameters && types.Add(type);

    private static void AddByName(HashSet<Type> types, string assemblyQualifiedName)
    {
        var type = Type.GetType(assemblyQualifiedName, throwOnError: false);
        if (type != null)
            Add(types, type);
    }

    private static void TryAddInstantiation(HashSet<Type> types, Type definition, Type argument)
    {
        try {
            Add(types, definition.MakeGenericType(argument));
        }
        catch (Exception e) when (e is ArgumentException or TypeLoadException) {
            // The argument violates a constraint - nothing to keep.
        }
    }

    private static Type? FindType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            var type = assembly.GetType(fullName, throwOnError: false);
            if (type != null)
                return type;
        }
        return null;
    }

    private static IEnumerable<MethodBase> GetMethods(Type type)
    {
        MethodBase[] methods;
        MethodBase[] constructors;
        try {
            methods = type.GetMethods(MemberFlags);
            constructors = type.GetConstructors(MemberFlags);
        }
        catch (Exception e) when (e is TypeLoadException or FileNotFoundException or NotSupportedException) {
            yield break;
        }
        foreach (var method in methods)
            yield return method;
        foreach (var constructor in constructors)
            yield return constructor;
    }

    private static bool CanEmit(MethodBase method)
    {
        if (method.IsAbstract || method.ContainsGenericParameters)
            return false;
        if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0)
            return false;
        if ((method.MethodImplementationFlags & (MethodImplAttributes.InternalCall | MethodImplAttributes.Native)) != 0)
            return false;

        foreach (var parameter in method.GetParameters()) {
            if (!CanEmit(parameter))
                return false;
        }
        return method is not MethodInfo { ReturnParameter: { } returnParameter } || CanEmit(returnParameter);
    }

    private static bool CanEmit(ParameterInfo parameter)
    {
        if (parameter.GetRequiredCustomModifiers().Length > 0 || parameter.GetOptionalCustomModifiers().Length > 0)
            return false;

        var type = parameter.ParameterType;
        while (type.HasElementType) {
            if (type.IsPointer || type.IsFunctionPointer)
                return false;
            type = type.GetElementType()!;
        }
        return true;
    }
}
