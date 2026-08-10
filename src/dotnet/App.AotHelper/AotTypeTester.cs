using ActualChat.Aot;
using static System.Console;

namespace ActualChat.App.AotHelper;

public static class AotTypeTester
{
    private static readonly string[] RequiredAssemblies = [
        "ActualChat.Core",
        "ActualChat.Api",
        "ActualChat.Api.Contracts",
        "ActualChat.UI.Blazor",
        "ActualChat.UI.Blazor.App",
    ];

    private static readonly IAotTypeTester[] Testers = [
        new ComponentTypeTester(),
        new ApiTypeTester(),
        new SerializableTypeTester(),
    ];

    public static int RunTests()
    {
        EnsureAssembliesLoaded();

        var all = AotTypes.All;
        var failCount = 0;
        var totalCount = 0;

        foreach (var tester in Testers) {
            var types = all.Where(x => x.Value == tester.Kind).Select(x => x.Key).ToList();
            totalCount += types.Count;
            WriteLine($"[{tester.Kind}] Testing {types.Count} types...");
            foreach (var type in types) {
                if (!tester.Test(type))
                    failCount++;
            }
        }

        WriteLine();
        if (failCount > 0) {
            Error.WriteLine($"FAILED: {failCount} type(s) failed AOT validation.");
            return 1;
        }

        WriteLine($"OK: All {totalCount} types passed AOT validation.");
        return 0;
    }

    public static object? TryCreateInstance(Type type)
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

    /// <summary>
    /// Ensures all required assemblies are loaded and their module initializers have run.
    /// In JIT mode, Assembly.Load triggers module initializers.
    /// In Native AOT, we force-register AotSources directly so ILC includes the code.
    /// </summary>
    public static void EnsureAssembliesLoaded()
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetName().Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var name in RequiredAssemblies) {
            if (loaded.Contains(name)) continue;
            try { Assembly.Load(name); }
            catch { /* Expected in Native AOT */ }
        }

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

        // Fallback for Native AOT: explicit registration (deduplicates by type)
        AotTypes.AddSource(new ActualChat.Module.CoreAotSource());
        AotTypes.AddSource(new ActualChat.Module.ApiAotSource());
        AotTypes.AddSource(new ActualChat.Module.ApiContractsAotSource());
        AotTypes.AddSource(new ActualChat.UI.Blazor.Module.BlazorUIAotSource());
        AotTypes.AddSource(new ActualChat.UI.Blazor.App.Module.BlazorUIAppAotSource());
    }
}
