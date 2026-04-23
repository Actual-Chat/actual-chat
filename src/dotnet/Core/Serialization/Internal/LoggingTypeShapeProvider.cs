using System.Collections.Concurrent;

namespace ActualChat.Serialization.Internal;

/// <summary>
/// Diagnostic decorator: forwards <see cref="GetTypeShape"/> to <paramref name="inner"/>,
/// logs the assembly-qualified name of any type for which the inner provider returns null
/// (deduplicated). Use only for triage — wrap <see cref="Serializers"/>'s composite provider
/// to surface missing-witness failures as readable output.
/// </summary>
public sealed class LoggingTypeShapeProvider(ITypeShapeProvider inner) : ITypeShapeProvider
{
    private readonly ConcurrentDictionary<Type, byte> _logged = new();

    public ITypeShape? GetTypeShape(Type type)
    {
        var shape = inner.GetTypeShape(type);
        if (shape is null && _logged.TryAdd(type, 0))
            Console.Error.WriteLine($"MISSING SHAPE: {type.AssemblyQualifiedName}");
        return shape;
    }
}
