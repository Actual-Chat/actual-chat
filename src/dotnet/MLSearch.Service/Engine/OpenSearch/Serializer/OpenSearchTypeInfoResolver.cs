
using System.Text.Json.Serialization.Metadata;
using OpenSearch.Client;

namespace ActualChat.MLSearch.Engine.OpenSearch.Serializer;

public class OpenSearchTypeInfoResolver : DefaultJsonTypeInfoResolver
{
    protected IConnectionSettingsValues ConnectionSettings { get; }

    public OpenSearchTypeInfoResolver(IConnectionSettingsValues connectionSettings)
    {
        ArgumentNullException.ThrowIfNull(connectionSettings);
        ConnectionSettings = connectionSettings;

        Modifiers.AddRange([
            ShouldSerializeModifier,
            PropertyOverrideModifier,
        ]);
    }

    private static void ShouldSerializeModifier(JsonTypeInfo typeInfo)
    {
        foreach (var property in typeInfo.Properties) {
            if (property.PropertyType == typeof(QueryContainer)) {
                property.ShouldSerialize = ShouldSerializeQueryContainer;
            }
            else if (typeof(IEnumerable<QueryContainer>).IsAssignableFrom(property.PropertyType)) {
                property.ShouldSerialize = ShouldSerializeQueryContainers;
            }
        }
    }

    private static bool ShouldSerializeQueryContainer(object o, object? value)
        => o is not null && value is IQueryContainer q && q.IsWritable;

    private static bool ShouldSerializeQueryContainers(object o, object? value)
        => o is not null && value is IEnumerable<QueryContainer> queryContainers
            && queryContainers.Any(q => (q as IQueryContainer)?.IsWritable ?? false);

    private void PropertyOverrideModifier(JsonTypeInfo typeInfo)
    {
        foreach (var property in typeInfo.Properties) {
            var member = property.AttributeProvider as MemberInfo;
            if (member is null || !ConnectionSettings.PropertyMappings.TryGetValue(member, out var propertyMapping)) {
                propertyMapping = OpenSearchPropertyAttributeBase.From(member);
            }

            var serializerMapping = member is null ? null : ConnectionSettings.PropertyMappingProvider?.CreatePropertyMapping(member);

            var nameOverride = propertyMapping?.Name ?? serializerMapping?.Name;
            if (!string.IsNullOrWhiteSpace(nameOverride)) {
                property.Name = nameOverride;
            }

            var overrideIgnore = propertyMapping?.Ignore ?? serializerMapping?.Ignore;
            if (overrideIgnore.HasValue) {
                property.ShouldSerialize = (_, _) => false;
            }
        }
    }
}
