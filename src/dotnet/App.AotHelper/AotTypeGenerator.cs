using System.Text;
using System.Text.RegularExpressions;
using ActualChat.Aot;
using Microsoft.AspNetCore.Components;
using static System.Console;

namespace ActualChat.App.AotHelper;

public static class AotTypeGenerator
{
    /// <summary>
    /// If <c>true</c>, emitted <c>CodeKeeper.Keep("&lt;AQN&gt;")</c> calls use the full
    /// assembly-qualified name (including <c>Version=..., Culture=..., PublicKeyToken=...</c>
    /// for every referenced assembly). If <c>false</c> (default), the version/culture/token
    /// are stripped and each assembly reference becomes a simple name (e.g.
    /// <c>"Ns.Type, MyAssembly"</c>).
    /// <para>
    /// Short form is accepted by <see cref="Type.GetType(string)"/> and is picked up by
    /// ILC's trimmer dataflow analysis — it cuts the generated <c>*AotSource.g.cs</c> files
    /// down dramatically. The long form is only needed if two strongly-named assemblies
    /// share the same simple name, which isn't our situation.
    /// </para>
    /// </summary>
    public static readonly bool UseVersionedAqns = false;

    // Matches `, Version=..., Culture=..., PublicKeyToken=...` anywhere in an AQN, including
    // inside nested `[[...]]` generic-arg AQNs. The three properties always appear in this
    // order per the CLR AQN format.
    private static readonly Regex AsmQualifierRegex = new(
        @",\s*Version=[^,\]]+,\s*Culture=[^,\]]+,\s*PublicKeyToken=[^,\]]+",
        RegexOptions.Compiled);

    private static string NormalizeAqn(string aqn)
        => UseVersionedAqns ? aqn : AsmQualifierRegex.Replace(aqn, "");

    // Assemblies to scan for components and APIs
    private static readonly string[] AssemblyNames = [
        "ActualChat.Core",
        "ActualChat.Api",
        "ActualChat.Api.Contracts",
        "ActualChat.UI.Blazor",
        "ActualChat.UI.Blazor.App",
    ];

    // Framework types that must be kept for Native AOT (emitted into CoreAotSource).
    // ArraySegment<byte> / <char> are referenced by HttpContent, BufferedFileStreamStrategy,
    // MemoryMarshal.TryGetArray, etc., and ILC can drop their generic instantiations
    // when nothing in our code keeps them.
    private static readonly string[] FrameworkTypeKeeps = [
        "global::System.ArraySegment<byte>",
        "global::System.ArraySegment<char>",
    ];

    // ActualLab.Serialization.MessagePackByteSerializer<T> is constructed through an
    // Expression<Func<...>> factory compiled per T. Under NativeAOT/trimming, every T that
    // may reach this factory needs its MessagePackByteSerializer / Func / Expression generic
    // instantiations kept explicitly. Primitives + universal framework types go into the Core
    // AotSource. Additional per-target type arguments (enums, generic instantiations like
    // ApiArray<T>, DTOs) are discovered at generation time by walking Serializable roots.
    private static readonly string[] MessagePackByteSerializerCoreTypeArgs = [
        "bool", "byte", "sbyte", "short", "ushort",
        "int", "uint", "long", "ulong",
        "float", "double", "decimal",
        "char", "string",
        "global::System.DateTime", "global::System.DateTimeOffset",
        "global::System.TimeSpan", "global::System.DateOnly", "global::System.TimeOnly",
        "global::System.Guid",
        "global::System.Reactive.Unit",
        "global::ActualLab.Text.Symbol",
        "global::ActualLab.Time.Moment",
        "global::ActualLab.Reflection.TypeRef",
    ];

    // Target projects: assembly name -> (class name, namespace, relative path from src/dotnet, type kinds)
    private static readonly AotSourceTarget[] Targets = [
        new("ActualChat.Core",
            "CoreAotSource",
            "ActualChat.Module",
            "Core/Module/CoreAotSource.g.cs",
            [AotTypeKind.Api, AotTypeKind.Serializable]),
        new("ActualChat.Api",
            "ApiAotSource",
            "ActualChat.Module",
            "Api/Module/ApiAotSource.g.cs",
            [AotTypeKind.Serializable]),
        new("ActualChat.Api.Contracts",
            "ApiContractsAotSource",
            "ActualChat.Module",
            "Api.Contracts/Module/ApiContractsAotSource.g.cs",
            [AotTypeKind.Api, AotTypeKind.Serializable]),
        new("ActualChat.UI.Blazor",
            "BlazorUIAotSource",
            "ActualChat.UI.Blazor.Module",
            "UI.Blazor/Module/BlazorUIAotSource.g.cs",
            [AotTypeKind.Component]),
        new("ActualChat.UI.Blazor.App",
            "BlazorUIAppAotSource",
            "ActualChat.UI.Blazor.App.Module",
            "UI.Blazor.App/Module/BlazorUIAppAotSource.g.cs",
            [AotTypeKind.Component, AotTypeKind.Serializable]),
    ];

    public static int Generate(string? projectRoot)
    {
        projectRoot ??= FindProjectRoot();
        if (projectRoot == null) {
            Error.WriteLine("Error: Could not find project root (no ActualChat.sln found in any parent directory).");
            return 1;
        }

        var srcDotnet = Path.Combine(projectRoot, "src", "dotnet");
        if (!Directory.Exists(srcDotnet)) {
            Error.WriteLine($"Error: src/dotnet directory not found at: {srcDotnet}");
            return 1;
        }

        LoadAssemblies();
        var components = DiscoverTypes(typeof(ComponentBase), includeAbstract: false, interfacesOnly: false);
        var apis = DiscoverTypes(typeof(IComputeService), includeAbstract: true, interfacesOnly: true);
        var serializables = DiscoverSerializableTypes();

        WriteLine($"Discovered {components.Count} Blazor components");
        WriteLine($"Discovered {apis.Count} API interfaces");
        WriteLine($"Discovered {serializables.Count} serializable types");

        // Discover STJ converter types needed for JS interop
        WriteLine("Discovering STJ converter types...");
        var stjConverters = StjConverterDiscovery.DiscoverAll();
        WriteLine($"Discovered {stjConverters.Count} STJ converter types");

        // Discover Fusion ComputedStateComponent state types
        var stateFactoryKeeps = DiscoverComputedStateFactoryKeeps();
        WriteLine($"Discovered {stateFactoryKeeps.Count} ComputedStateComponent state factory types");
        foreach (var aqn in stateFactoryKeeps)
            stjConverters.Add(aqn);

        foreach (var target in Targets) {
            // Collect types from all requested kinds, filtered to the target assembly
            var filtered = new List<(Type Type, AotTypeKind Kind)>();
            foreach (var kind in target.TypeKinds) {
                var source = kind switch {
                    AotTypeKind.Api => apis,
                    AotTypeKind.Component => components,
                    AotTypeKind.Serializable => serializables,
                    _ => [],
                };
                filtered.AddRange(source
                    .Where(t => t.Assembly.GetName().Name == target.AssemblyName)
                    .Select(t => (t, kind)));
            }

            // STJ converter keeps go into the UI.Blazor AotSource
            var stjKeeps = target.AssemblyName == "ActualChat.UI.Blazor" ? stjConverters : null;

            // Framework type keeps go into the Core AotSource
            var frameworkKeeps = target.AssemblyName == "ActualChat.Core" ? FrameworkTypeKeeps : null;

            // MessagePackByteSerializer<T> keeps: static primitives in Core; reachable-type
            // discovery runs per target over its Serializable roots + API interfaces.
            IReadOnlyList<string>? mpByteSerializerTypeArgs = null;
            var mpRoots = filtered
                .Where(x => x.Kind == AotTypeKind.Serializable)
                .Select(x => x.Type)
                .ToList();
            var mpApiRoots = filtered
                .Where(x => x.Kind == AotTypeKind.Api)
                .Select(x => x.Type)
                .ToList();
            if (target.AssemblyName == "ActualChat.Core" || mpRoots.Count > 0 || mpApiRoots.Count > 0) {
                var discovered = MessagePackByteSerializerDiscovery.DiscoverAll(mpRoots, mpApiRoots);
                var combined = new SortedSet<string>(discovered, StringComparer.Ordinal);
                if (target.AssemblyName == "ActualChat.Core") {
                    foreach (var t in MessagePackByteSerializerCoreTypeArgs)
                        combined.Add(t);
                }
                if (combined.Count > 0) {
                    mpByteSerializerTypeArgs = combined.ToList();
                    WriteLine($"  [{target.AssemblyName}] MessagePackByteSerializer<T> keeps: {mpByteSerializerTypeArgs.Count}");
                }
            }

            // Discover MessagePack formatter types required by all serializable root types
            // in this target assembly (walks member graph across assemblies).
            var mpKeeps = (mpRoots.Count > 0 || mpApiRoots.Count > 0)
                ? MessagePackFormatterDiscovery.DiscoverAll(mpRoots, mpApiRoots)
                : null;
            if (mpKeeps is { Count: > 0 })
                WriteLine($"  [{target.AssemblyName}] Discovered {mpKeeps.Count} MessagePack formatter types");

            var outputPath = Path.Combine(srcDotnet, target.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var code = GenerateSourceFile(target, filtered, stjKeeps, mpKeeps, frameworkKeeps, mpByteSerializerTypeArgs);
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            // StringBuilder.AppendLine emits CRLF on Windows, but .gitattributes stores
            // *.cs with LF - without this the file is rewritten dirty on every run.
            File.WriteAllText(outputPath, code.ReplaceLineEndings("\n"), Encoding.UTF8);
            WriteLine($"Generated: {target.RelativePath} ({filtered.Count} types)");
        }

        return 0;
    }

    private static string? FindProjectRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir != null) {
            if (File.Exists(Path.Combine(dir, "ActualChat.sln")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static void LoadAssemblies()
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetName().Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var name in AssemblyNames) {
            if (loaded.Contains(name))
                continue;
            try {
                var assembly = Assembly.Load(name);
                WriteLine($"Loaded: {assembly.GetName().Name}");
            }
            catch (Exception e) {
                Error.WriteLine($"Warning: Could not load {name}: {e.Message}");
            }
        }
    }

    private static List<Type> DiscoverTypes(Type baseType, bool includeAbstract, bool interfacesOnly)
    {
        var result = new List<Type>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            var name = assembly.GetName().Name ?? "";
            if (!name.StartsWith("ActualChat.", StringComparison.Ordinal))
                continue;

            try {
                foreach (var type in assembly.GetTypes()) {
                    if (interfacesOnly && !type.IsInterface)
                        continue;
                    if (!interfacesOnly && (type.IsInterface || type.IsGenericTypeDefinition))
                        continue;
                    if (!includeAbstract && type.IsAbstract)
                        continue;
                    if (!baseType.IsAssignableFrom(type))
                        continue;
                    if (type == baseType)
                        continue;
                    result.Add(type);
                }
            }
            catch (ReflectionTypeLoadException e) {
                foreach (var type in e.Types) {
                    if (type == null)
                        continue;
                    if (interfacesOnly && !type.IsInterface)
                        continue;
                    if (!interfacesOnly && (type.IsInterface || type.IsGenericTypeDefinition))
                        continue;
                    if (!includeAbstract && type.IsAbstract)
                        continue;
                    if (!baseType.IsAssignableFrom(type))
                        continue;
                    if (type == baseType)
                        continue;
                    result.Add(type);
                }
            }
        }

        result.Sort((a, b) => string.Compare(
            a.FullName, b.FullName, StringComparison.Ordinal));
        return result;
    }

    /// <summary>
    /// Discovers types marked with [MessagePackObject], [MessagePackFormatter], or [Union]
    /// that are used as serializable DTOs (commands, results, etc.).
    /// </summary>
    private static List<Type> DiscoverSerializableTypes()
    {
        var result = new List<Type>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            var name = assembly.GetName().Name ?? "";
            if (!name.StartsWith("ActualChat.", StringComparison.Ordinal))
                continue;

            try {
                foreach (var type in assembly.GetTypes()) {
                    if (type.IsGenericTypeDefinition)
                        continue;
                    if ((type.IsAbstract || type.IsInterface) && !HasMessagePackUnionMarker(type))
                        continue;
                    if (!HasMessagePackMarker(type))
                        continue;
                    result.Add(type);
                }
            }
            catch (ReflectionTypeLoadException e) {
                foreach (var type in e.Types) {
                    if (type == null || type.IsGenericTypeDefinition)
                        continue;
                    if ((type.IsAbstract || type.IsInterface) && !HasMessagePackUnionMarker(type))
                        continue;
                    if (!HasMessagePackMarker(type))
                        continue;
                    result.Add(type);
                }
            }
        }

        result.Sort((a, b) => string.Compare(
            a.FullName, b.FullName, StringComparison.Ordinal));
        return result;
    }

    private static bool HasMessagePackMarker(Type type)
    {
        foreach (var a in type.CustomAttributes) {
            var n = a.AttributeType.FullName;
            if (n is "MessagePack.MessagePackObjectAttribute"
                or "MessagePack.MessagePackFormatterAttribute"
                or "MessagePack.UnionAttribute")
                return true;
        }
        return false;
    }

    private static bool HasMessagePackUnionMarker(Type type)
    {
        foreach (var a in type.CustomAttributes) {
            if (a.AttributeType.FullName == "MessagePack.UnionAttribute")
                return true;
        }
        return false;
    }

    private static string GenerateSourceFile(
        AotSourceTarget target,
        List<(Type Type, AotTypeKind Kind)> types,
        SortedSet<string>? stjConverterAqns = null,
        SortedSet<string>? mpFormatterAqns = null,
        IReadOnlyList<string>? frameworkTypeKeeps = null,
        IReadOnlyList<string>? mpByteSerializerTypeArgs = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("// This file is generated by App.AotHelper -g. Do not edit manually.");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine();
        sb.AppendLine("#pragma warning disable CS0618 // Obsolete types are still needed for AOT retention");
        sb.AppendLine();
        sb.AppendLine("using ActualChat.Aot;");
        sb.AppendLine();
        sb.AppendLine($"namespace {target.Namespace};");
        sb.AppendLine();
        sb.AppendLine($"internal partial class {target.ClassName} : IAotSource");
        sb.AppendLine("{");

        // KeepTypes method
        sb.AppendLine("    public void KeepTypes()");
        sb.AppendLine("    {");
        var hasContent = types.Count > 0
            || stjConverterAqns is { Count: > 0 }
            || mpFormatterAqns is { Count: > 0 }
            || frameworkTypeKeeps is { Count: > 0 }
            || mpByteSerializerTypeArgs is { Count: > 0 };
        if (hasContent) {
            sb.AppendLine("        if (CodeKeeper.AlwaysTrue)");
            sb.AppendLine("            return;");
            sb.AppendLine();
            foreach (var (type, kind) in types) {
                var typeName = FormatTypeName(type);
                if (typeName == null) continue;
                var method = kind == AotTypeKind.Serializable ? "KeepSerializable" : "Keep";
                sb.AppendLine($"        CodeKeeper.{method}<{typeName}>();");
            }
            if (frameworkTypeKeeps is { Count: > 0 }) {
                sb.AppendLine();
                sb.AppendLine("        // Framework types referenced by BCL / runtime code paths");
                foreach (var typeName in frameworkTypeKeeps)
                    sb.AppendLine($"        CodeKeeper.Keep<{typeName}>();");
            }
            if (mpByteSerializerTypeArgs is { Count: > 0 }) {
                sb.AppendLine();
                sb.AppendLine("        // MessagePackByteSerializer<T> generic instantiations used by the");
                sb.AppendLine("        // Expression<Func<...>> factory inside ActualLab.Serialization.");
                foreach (var typeArg in mpByteSerializerTypeArgs) {
                    sb.AppendLine($"        CodeKeeper.Keep<global::ActualLab.Serialization.MessagePackByteSerializer<{typeArg}>>();");
                    sb.AppendLine($"        CodeKeeper.Keep<global::System.Func<global::MessagePack.MessagePackSerializerOptions, global::System.Type, global::ActualLab.Serialization.MessagePackByteSerializer<{typeArg}>>>();");
                    sb.AppendLine($"        CodeKeeper.Keep<global::System.Linq.Expressions.Expression<global::System.Func<global::MessagePack.MessagePackSerializerOptions, global::System.Type, global::ActualLab.Serialization.MessagePackByteSerializer<{typeArg}>>>>();");
                }
            }
            if (mpFormatterAqns is { Count: > 0 }) {
                sb.AppendLine();
                sb.AppendLine("        // MessagePack formatter types for serializable root types (auto-discovered)");
                foreach (var aqn in mpFormatterAqns)
                    sb.AppendLine($"        CodeKeeper.Keep(\"{EscapeString(NormalizeAqn(aqn))}\");");
            }
            if (stjConverterAqns is { Count: > 0 }) {
                sb.AppendLine();
                sb.AppendLine("        // STJ internal converter types for JS interop (auto-discovered)");
                foreach (var aqn in stjConverterAqns)
                    sb.AppendLine($"        CodeKeeper.Keep(\"{EscapeString(NormalizeAqn(aqn))}\");");
            }
        }
        sb.AppendLine("    }");
        sb.AppendLine();

        // ListTypes method
        sb.AppendLine($"    public (Type, AotTypeKind)[] ListTypes()");
        sb.AppendLine("        => [");
        foreach (var (type, kind) in types) {
            var typeName = FormatTypeName(type);
            if (typeName != null)
                sb.AppendLine($"            (typeof({typeName}), AotTypeKind.{kind}),");
        }
        sb.AppendLine("        ];");

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string? FormatTypeName(Type type)
    {
        if (type.FullName == null)
            return null;

        var ns = type.Namespace;
        var name = type.Name;
        return ns == null ? $"global::{name}" : $"global::{ns}.{name}";
    }

    /// <summary>
    /// Discovers state types T from all ComputedStateComponent&lt;T&gt; descendants
    /// and returns CodeKeeper.Keep AQNs for CreateDefaultStateOptionsFactory&lt;T&gt;.
    /// </summary>
    private static List<string> DiscoverComputedStateFactoryKeeps()
    {
        var computedStateComponentType = typeof(ActualLab.Fusion.Blazor.ComputedStateComponent<>);
        var factoryTypeName = "ActualLab.Fusion.Blazor.ComputedStateComponent+CreateDefaultStateOptionsFactory`1";
        var factoryAssembly = "ActualLab.Fusion.Blazor";
        var stateTypes = new HashSet<Type>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            var name = assembly.GetName().Name ?? "";
            if (!name.StartsWith("ActualChat.", StringComparison.Ordinal))
                continue;

            try {
                foreach (var type in assembly.GetTypes()) {
                    if (type.IsAbstract || type.IsGenericTypeDefinition)
                        continue;

                    // Walk the inheritance chain to find ComputedStateComponent<T>
                    var current = type.BaseType;
                    while (current != null) {
                        if (current.IsGenericType
                            && current.GetGenericTypeDefinition() == computedStateComponentType) {
                            var stateType = current.GetGenericArguments()[0];
                            if (!stateType.IsGenericParameter)
                                stateTypes.Add(stateType);
                            break;
                        }
                        current = current.BaseType;
                    }
                }
            }
            catch (ReflectionTypeLoadException) { }
        }

        var result = new List<string>();
        foreach (var stateType in stateTypes.OrderBy(t => t.FullName)) {
            var stateAqn = stateType.AssemblyQualifiedName;
            if (stateAqn == null)
                continue;
            var factoryAqn = $"{factoryTypeName}[[{stateAqn}]], {factoryAssembly}";
            result.Add(factoryAqn);
        }
        return result;
    }

    private static string EscapeString(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private record AotSourceTarget(
        string AssemblyName,
        string ClassName,
        string Namespace,
        string RelativePath,
        AotTypeKind[] TypeKinds);
}
