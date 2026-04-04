using ActualChat.Aot;
using Microsoft.AspNetCore.Components;
using static System.Console;

namespace ActualChat.App.AotHelper;

public static class AotTypeTester
{
    // Assemblies that must be loaded for their module initializers to register AotSources
    private static readonly string[] RequiredAssemblies = [
        "ActualChat.Core",
        "ActualChat.Api",
        "ActualChat.Api.Contracts",
        "ActualChat.UI.Blazor",
        "ActualChat.UI.Blazor.App",
    ];

    public static int RunTests()
    {
        EnsureAssembliesLoaded();

        var all = AotTypes.All;
        var components = all.Where(x => x.Value == AotTypeKind.Component).Select(x => x.Key).ToList();
        var apis = all.Where(x => x.Value == AotTypeKind.Api).Select(x => x.Key).ToList();

        WriteLine($"Testing {components.Count} components and {apis.Count} APIs...");
        WriteLine();

        var failCount = 0;

        foreach (var type in components) {
            if (!TestComponent(type))
                failCount++;
        }

        foreach (var type in apis) {
            if (!TestApi(type))
                failCount++;
        }

        WriteLine();
        if (failCount > 0) {
            Error.WriteLine($"FAILED: {failCount} type(s) failed AOT validation.");
            return 1;
        }

        WriteLine($"OK: All {components.Count + apis.Count} types passed AOT validation.");
        return 0;
    }

    /// <summary>
    /// Ensures all required assemblies are loaded and their module initializers have run.
    /// In JIT mode, Assembly.Load triggers module initializers.
    /// In Native AOT, all assemblies are statically linked - we use typeof() references
    /// in a dead branch to force ILC to include the module initializers, and
    /// RunModuleConstructor to trigger their execution at runtime.
    /// </summary>
    /// <summary>
    /// Ensures all required assemblies are loaded and their module initializers have run.
    /// In JIT mode, Assembly.Load triggers module initializers.
    /// In Native AOT, we force-register AotSources directly so ILC includes the code.
    /// </summary>
    private static void EnsureAssembliesLoaded()
    {
        // In JIT mode, load assemblies that aren't loaded yet
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetName().Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var name in RequiredAssemblies) {
            if (loaded.Contains(name)) continue;
            try { Assembly.Load(name); }
            catch { /* Expected in Native AOT */ }
        }

        // Force module initializers for all loaded ActualChat assemblies
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            var name = assembly.GetName().Name ?? "";
            if (!name.StartsWith("ActualChat.", StringComparison.Ordinal))
                continue;
            try {
                foreach (var module in assembly.GetModules())
                    RuntimeHelpers.RunModuleConstructor(module.ModuleHandle);
            }
            catch { /* Best effort */ }
        }

        // Explicit registration as a fallback for Native AOT where dynamic assembly
        // loading doesn't work. ILC will see these constructor calls and include the types.
        // AddSource deduplicates by source type, so these are safe even if module initializers ran.
        AotTypes.AddSource(new ActualChat.Internal.CoreAotSource());
        AotTypes.AddSource(new ActualChat.Internal.ApiContractsAotSource());
        AotTypes.AddSource(new ActualChat.UI.Blazor.Internal.BlazorUIAotSource());
        AotTypes.AddSource(new ActualChat.UI.Blazor.App.Internal.BlazorUIAppAotSource());
    }

    private static bool TestComponent(Type type)
    {
        var shortName = type.FullName ?? type.Name;
        try {
            // 1. Verify it's a ComponentBase
            if (!typeof(ComponentBase).IsAssignableFrom(type)) {
                Error.WriteLine($"FAIL [Component] {shortName}: Not a ComponentBase");
                return false;
            }

            // 2. Try to create an instance (parameterless constructor)
            // Many components require DI, so instantiation failure is expected -
            // we still verify the constructor is visible to reflection.
            var instance = TryCreateInstance(type);
            var ctors = type.GetConstructors(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (instance == null && ctors.Length == 0) {
                Error.WriteLine($"FAIL [Component] {shortName}: No constructors found (metadata trimmed?)");
                return false;
            }

            // 3. Enumerate properties (verifies reflection metadata is retained)
            var props = type.GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (props.Length == 0)
                Error.WriteLine($"WARN [Component] {shortName}: No properties found (metadata may be trimmed)");

            // 4. Check [Parameter] properties are accessible
            var parameterProps = props.Where(p =>
                p.GetCustomAttributes(typeof(ParameterAttribute), true).Length > 0).ToList();

            foreach (var prop in parameterProps) {
                if (prop.GetMethod == null)
                    Error.WriteLine($"WARN [Component] {shortName}.{prop.Name}: No getter");
                if (prop.SetMethod == null)
                    Error.WriteLine($"WARN [Component] {shortName}.{prop.Name}: No setter");
            }

            // 5. Enumerate methods
            var methods = type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            var instantiated = instance != null ? "yes" : "no";
            WriteLine($"  OK [Component] {shortName} " +
                $"(instantiated: {instantiated}, props: {props.Length}, params: {parameterProps.Count}, methods: {methods.Length})");
            return true;
        }
        catch (Exception e) {
            Error.WriteLine($"FAIL [Component] {shortName}: {e.Message}");
            return false;
        }
    }

    private static bool TestApi(Type type)
    {
        var shortName = type.FullName ?? type.Name;
        try {
            // 1. Must be an interface
            if (!type.IsInterface) {
                Error.WriteLine($"FAIL [API] {shortName}: Not an interface");
                return false;
            }

            // 2. Enumerate methods (verifies reflection metadata is retained)
            var methods = type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance);
            if (methods.Length == 0)
                Error.WriteLine($"WARN [API] {shortName}: No methods found (metadata may be trimmed)");

            // 3. Verify each method's parameter types and return type are loadable
            foreach (var method in methods) {
                var returnType = method.ReturnType;
                if (returnType == null!)
                    Error.WriteLine($"WARN [API] {shortName}.{method.Name}: Return type is null");

                foreach (var param in method.GetParameters()) {
                    if (param.ParameterType == null!)
                        Error.WriteLine($"WARN [API] {shortName}.{method.Name}: Parameter '{param.Name}' type is null");
                }
            }

            WriteLine($"  OK [API] {shortName} (methods: {methods.Length})");
            return true;
        }
        catch (Exception e) {
            Error.WriteLine($"FAIL [API] {shortName}: {e.Message}");
            return false;
        }
    }

    private static object? TryCreateInstance(Type type)
    {
        try {
            var ctor = type.GetConstructor(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null, types: Type.EmptyTypes, modifiers: null);

            if (ctor != null)
                return ctor.Invoke(null);

            return Activator.CreateInstance(type, nonPublic: true);
        }
        catch {
            return null;
        }
    }
}
