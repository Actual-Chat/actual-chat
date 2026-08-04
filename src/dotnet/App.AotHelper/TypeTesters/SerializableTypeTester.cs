using ActualChat.Aot;
using static System.Console;

namespace ActualChat.App.AotHelper;

public sealed class SerializableTypeTester : IAotTypeTester
{
    public AotTypeKind Kind => AotTypeKind.Serializable;

    public bool Test(Type type)
    {
        var shortName = type.FullName ?? type.Name;
        try {
            // A [Union] root can be an interface, which never has constructors; the concrete
            // subtypes carry them and are registered — and so tested — separately.
            if (type.IsInterface) {
                WriteLine($"  OK [Serializable] {shortName} (union root)");
                return true;
            }

            var ctors = type.GetConstructors(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (ctors.Length == 0) {
                Error.WriteLine($"FAIL [Serializable] {shortName}: No constructors found (metadata trimmed?)");
                return false;
            }

            var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var instance = AotTypeTester.TryCreateInstance(type);

            var instantiated = instance != null ? "yes" : "no";
            WriteLine($"  OK [Serializable] {shortName} " +
                $"(instantiated: {instantiated}, props: {props.Length}, ctors: {ctors.Length})");
            return true;
        }
        catch (Exception e) {
            Error.WriteLine($"FAIL [Serializable] {shortName}: {e.Message}");
            return false;
        }
    }
}
