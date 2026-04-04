using System.Text.Json.Serialization.Metadata;

namespace ActualChat.UI.Blazor;

/// <summary>
/// Wraps multiple <see cref="IJsonTypeInfoResolver"/> instances (typically <see cref="JsonSerializerContext"/>)
/// and returns null instead of throwing for types they don't support.
/// This allows them to be inserted before the default reflection resolver in the chain.
/// </summary>
public sealed class SafeCompositeResolver(IJsonTypeInfoResolver[] resolvers) : IJsonTypeInfoResolver
{
    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        foreach (var resolver in resolvers) {
            try {
                var typeInfo = resolver.GetTypeInfo(type, options);
                if (typeInfo != null)
                    return typeInfo;
            }
            catch (NotSupportedException) {
                // JsonSerializerContext throws for unknown types — swallow and try next
            }
        }
        return null;
    }
}
