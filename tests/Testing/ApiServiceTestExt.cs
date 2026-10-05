namespace ActualChat.Testing;

public static class ApiServiceTestExt
{
    public static void AssertNoSessionContext(this Type serviceType)
    {
        var violations = new List<string>();
        var visited = new HashSet<Type>();
        foreach (var method in serviceType.GetMethods())
            foreach (var parameter in method.GetParameters())
                Inspect(parameter.ParameterType, parameter.Name ?? method.Name);

        violations.Should().BeEmpty("backend {0} must not receive session context", serviceType.FullName);
        return;

        void Inspect(Type type, string name) {
            if (type == typeof(Session) || name.Contains("session", StringComparison.OrdinalIgnoreCase)) {
                violations.Add(name);
                return;
            }
            if (!visited.Add(type))
                return;

            foreach (var argument in type.GetGenericArguments())
                Inspect(argument, name);
            if (!(type.Namespace ?? "").StartsWith("ActualChat"))
                return;

            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                Inspect(property.PropertyType, property.Name);
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
                Inspect(field.FieldType, field.Name);
        }
    }

    public static void AssertNoStorageDependencies(this Type serviceType)
    {
        var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var dependencies = serviceType.GetFields(flags).Select(f => f.FieldType)
            .Concat(serviceType.GetProperties(flags).Select(p => p.PropertyType))
            .ToList();
        for (var baseType = serviceType.BaseType; baseType is not null; baseType = baseType.BaseType)
            dependencies.Add(baseType);

        dependencies.Where(IsStorageType).Should().BeEmpty(
            "API service {0} must delegate persistence to backend APIs", serviceType.FullName);
        return;

        static bool IsStorageType(Type type) {
            var ns = type.Namespace ?? "";
            return ns.EndsWith(".Db")
                || ns.Contains(".EntityFramework")
                || ns == "ActualLab.Redis"
                || ns == "StackExchange.Redis"
                || (ns == "OpenIddict.Abstractions" && type.Name.EndsWith("Manager"))
                || type.GetGenericArguments().Any(IsStorageType);
        }
    }
}
