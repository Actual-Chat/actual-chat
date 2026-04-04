using System.Text;
using ActualChat.Aot;
using Microsoft.AspNetCore.Components;
using static System.Console;

namespace ActualChat.App.AotHelper;

public static class AotTypeGenerator
{
    // Assemblies to scan for components and APIs
    private static readonly string[] AssemblyNames = [
        "ActualChat.Core",
        "ActualChat.Api",
        "ActualChat.Api.Contracts",
        "ActualChat.UI.Blazor",
        "ActualChat.UI.Blazor.App",
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
            [AotTypeKind.Component]),
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

            var outputPath = Path.Combine(srcDotnet, target.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            var code = GenerateSourceFile(target, filtered);
            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(outputPath, code, Encoding.UTF8);
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
    /// Discovers types marked with [MemoryPackable] or [DataContract] that are
    /// used as serializable DTOs (commands, results, etc.).
    /// </summary>
    private static List<Type> DiscoverSerializableTypes()
    {
        var memoryPackableAttrName = "MemoryPack.MemoryPackableAttribute";
        var result = new List<Type>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            var name = assembly.GetName().Name ?? "";
            if (!name.StartsWith("ActualChat.", StringComparison.Ordinal))
                continue;

            try {
                foreach (var type in assembly.GetTypes()) {
                    if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                        continue;
                    // Look for [MemoryPackable] attribute (the primary serialization marker)
                    if (!type.CustomAttributes.Any(a =>
                        a.AttributeType.FullName == memoryPackableAttrName))
                        continue;
                    result.Add(type);
                }
            }
            catch (ReflectionTypeLoadException e) {
                foreach (var type in e.Types) {
                    if (type == null || type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                        continue;
                    if (!type.CustomAttributes.Any(a =>
                        a.AttributeType.FullName == memoryPackableAttrName))
                        continue;
                    result.Add(type);
                }
            }
        }

        result.Sort((a, b) => string.Compare(
            a.FullName, b.FullName, StringComparison.Ordinal));
        return result;
    }

    private static string GenerateSourceFile(AotSourceTarget target, List<(Type Type, AotTypeKind Kind)> types)
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
        if (types.Count > 0) {
            sb.AppendLine("        if (CodeKeeper.AlwaysTrue)");
            sb.AppendLine("            return;");
            sb.AppendLine();
            foreach (var (type, kind) in types) {
                var typeName = FormatTypeName(type);
                if (typeName == null) continue;
                // Use KeepSerializable for serializable types to also retain serializer infrastructure
                var method = kind == AotTypeKind.Serializable ? "KeepSerializable" : "Keep";
                sb.AppendLine($"        CodeKeeper.{method}<{typeName}>();");
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

    private record AotSourceTarget(
        string AssemblyName,
        string ClassName,
        string Namespace,
        string RelativePath,
        AotTypeKind[] TypeKinds);
}
