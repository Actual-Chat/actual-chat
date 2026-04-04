using ActualChat.Aot;
using static System.Console;

namespace ActualChat.App.AotHelper;

public sealed class ApiTypeTester : IAotTypeTester
{
    public AotTypeKind Kind => AotTypeKind.Api;

    public bool Test(Type type)
    {
        var shortName = type.FullName ?? type.Name;
        try {
            if (!type.IsInterface) {
                Error.WriteLine($"FAIL [API] {shortName}: Not an interface");
                return false;
            }

            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);

            foreach (var method in methods) {
                if (method.ReturnType == null!)
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
}
