using ActualChat.Aot;
using Microsoft.AspNetCore.Components;
using static System.Console;

namespace ActualChat.App.AotHelper;

public sealed class ComponentTypeTester : IAotTypeTester
{
    public AotTypeKind Kind => AotTypeKind.Component;

    public bool Test(Type type)
    {
        var shortName = type.FullName ?? type.Name;
        try {
            if (!typeof(ComponentBase).IsAssignableFrom(type)) {
                Error.WriteLine($"FAIL [Component] {shortName}: Not a ComponentBase");
                return false;
            }

            var instance = AotTypeTester.TryCreateInstance(type);
            var ctors = type.GetConstructors(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (instance == null && ctors.Length == 0) {
                Error.WriteLine($"FAIL [Component] {shortName}: No constructors found (metadata trimmed?)");
                return false;
            }

            var props = type.GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var parameterProps = props.Where(p =>
                p.GetCustomAttributes(typeof(ParameterAttribute), true).Length > 0).ToList();
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
}
