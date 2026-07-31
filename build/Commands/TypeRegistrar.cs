using System.Diagnostics.CodeAnalysis;
using Spectre.Console.Cli;

namespace Build.Commands;

// Spectre's own registrar can't hand CliContext to commands, so this minimal one
// resolves registered instances and falls back to Activator for the commands.
internal sealed class TypeRegistrar : ITypeRegistrar
{
    private Dictionary<Type, object> Instances { get; } = [];

    public void Register(Type service, Type implementation) { }
    public void RegisterInstance(Type service, object implementation) => Instances[service] = implementation;
    public void RegisterLazy(Type service, Func<object> factory) => Instances[service] = factory();
    public ITypeResolver Build() => new TypeResolver(Instances);

    // Nested types

    private sealed class TypeResolver(Dictionary<Type, object> instances) : ITypeResolver
    {
        private Dictionary<Type, object> Instances { get; } = instances;

        [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = "Build-time tool, never trimmed.")]
        [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "Build-time tool, never trimmed.")]
        public object? Resolve(Type? type)
        {
            if (type is null)
                return null;
            if (Instances.TryGetValue(type, out var instance))
                return instance;

            // Spectre asks for IEnumerable<T> for its own extension points.
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)) {
                var itemType = type.GetGenericArguments()[0];
                var items = Instances
                    .Where(x => itemType.IsAssignableFrom(x.Key))
                    .Select(x => x.Value)
                    .ToArray();
                var array = Array.CreateInstance(itemType, items.Length);
                items.CopyTo(array, 0);
                return array;
            }

            if (type.IsAbstract || type.IsInterface)
                return null;

            // Commands take their dependencies (CliContext) as constructor parameters.
            var constructor = type.GetConstructors()
                .OrderByDescending(x => x.GetParameters().Length)
                .FirstOrDefault();
            if (constructor is null)
                return Activator.CreateInstance(type);

            var arguments = constructor.GetParameters().Select(x => Resolve(x.ParameterType)).ToArray();
            return constructor.Invoke(arguments);
        }
    }
}
