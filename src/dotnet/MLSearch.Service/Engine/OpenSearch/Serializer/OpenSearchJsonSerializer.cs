using System.Text;
using ActualChat.MLSearch.Engine.OpenSearch.Serializer.Converters;
using OpenSearch.Client;
using OpenSearch.Net;

namespace ActualChat.MLSearch.Engine.OpenSearch.Serializer;
internal sealed class OpenSearchJsonSerializer : IOpenSearchSerializer, IPropertyMappingProvider
{
    private class CustomNamingPolicy(Func<string, string> nameInferer) : JsonNamingPolicy
    {
        public override string ConvertName(string name) => nameInferer.Invoke(name);
    }

    internal const int DefaultBufferSize = 1024;
    internal static readonly Encoding ExpectedEncoding = new UTF8Encoding(false);

    private readonly JsonSerializerOptions serializerOptions;
    private readonly JsonSerializerOptions indentedSerializerOptions;

    public OpenSearchJsonSerializer(
        IOpenSearchSerializer builtin,
        IConnectionSettingsValues settings,
        Func<JsonSerializerOptions>? jsonSerializerOptionsFactory = null
    )
    {
        var namingPolicy = settings.DefaultFieldNameInferrer is null
            ? null
            : new CustomNamingPolicy(settings.DefaultFieldNameInferrer);
        var memoryStreamFactory = settings.MemoryStreamFactory;
        var systemConverters = new JsonConverter[] {
            new JoinFieldConverter(builtin, memoryStreamFactory),
            new OscTypeConverter<QueryContainer>(builtin, memoryStreamFactory),
            new OscTypeConverter<CompletionField>(builtin, memoryStreamFactory),
            new OscTypeConverter<Attachment>(builtin, memoryStreamFactory),
            new OscTypeConverter<ILazyDocument>(builtin, memoryStreamFactory),
            new OscTypeConverter<LazyDocument>(builtin, memoryStreamFactory),
            new OscTypeConverter<GeoCoordinate>(builtin, memoryStreamFactory),
            new OscTypeConverter<GeoLocation>(builtin, memoryStreamFactory),
            new OscTypeConverter<CartesianPoint>(builtin, memoryStreamFactory),
            new OscTypeConverter<IGeoShape>(builtin, memoryStreamFactory),
            new OscTypeConverter<IGeometryCollection>(builtin, memoryStreamFactory),

            new StringEnumConverter(),
            new TimeSpanConverter(),
        };

        serializerOptions = CreateOptions(false);
        indentedSerializerOptions = CreateOptions(true);

        JsonSerializerOptions CreateOptions(bool writeIndented)
        {
            var options = jsonSerializerOptionsFactory?.Invoke() ?? new JsonSerializerOptions() {
                AllowTrailingCommas = true,
            };
            options.TypeInfoResolver = new OpenSearchTypeInfoResolver(settings);
            options.Converters.AddRange(systemConverters);
            if (namingPolicy is not null) {
                options.PropertyNamingPolicy = namingPolicy;
            }
            options.WriteIndented = writeIndented;

            return options;
        }
    }

    public object Deserialize(Type type, Stream stream) => JsonSerializer.Deserialize(stream, type, serializerOptions)!;

    public T Deserialize<T>(Stream stream) => JsonSerializer.Deserialize<T>(stream, serializerOptions)!;

    public async Task<object> DeserializeAsync(Type type, Stream stream, CancellationToken cancellationToken = default)
        => (await JsonSerializer.DeserializeAsync(stream, type, serializerOptions, cancellationToken).ConfigureAwait(false))!;

    public async Task<T> DeserializeAsync<T>(Stream stream, CancellationToken cancellationToken = default)
        => (await JsonSerializer.DeserializeAsync<T>(stream, serializerOptions, cancellationToken).ConfigureAwait(false))!;

    public void Serialize<T>(T data, Stream stream, SerializationFormatting formatting = SerializationFormatting.None)
    {
        var options = formatting == SerializationFormatting.Indented ? indentedSerializerOptions : serializerOptions;
        JsonSerializer.Serialize(stream, data, options);
    }

    public async Task SerializeAsync<T>(T data, Stream stream, SerializationFormatting formatting = SerializationFormatting.None, CancellationToken cancellationToken = default)
    {
        var options = formatting == SerializationFormatting.Indented ? indentedSerializerOptions : serializerOptions;
        await JsonSerializer.SerializeAsync(stream, data, options, cancellationToken).ConfigureAwait(false);
    }

    private readonly ConcurrentDictionary<string, IPropertyMapping?> _properties = new(StringComparer.Ordinal);

    public IPropertyMapping? CreatePropertyMapping(MemberInfo memberInfo)
    {
        var memberInfoString = $"{memberInfo.DeclaringType?.FullName}.{memberInfo.Name}";
        if (!_properties.TryGetValue(memberInfoString, out var mapping)) {
            mapping = FromAttributes(memberInfo);

            _properties.TryAdd(memberInfoString, mapping);
        }

        return mapping;
    }

    private static IPropertyMapping? FromAttributes(MemberInfo memberInfo)
    {
        var propertyName = memberInfo.GetCustomAttribute<PropertyNameAttribute>(true);
        var jsonPropertyName = memberInfo.GetCustomAttribute<JsonPropertyNameAttribute>();
        var newtonsoftJsonProperty = memberInfo.GetCustomAttribute<Newtonsoft.Json.JsonPropertyAttribute>(true);
        var dataMemberProperty = memberInfo.GetCustomAttribute<DataMemberAttribute>(true);

        var ignore = memberInfo.GetCustomAttribute<IgnoreAttribute>(true);
        var jsonIgnore = memberInfo.GetCustomAttribute<JsonIgnoreAttribute>(true);
        var newtonsoftJsonIgnore = memberInfo.GetCustomAttribute<Newtonsoft.Json.JsonIgnoreAttribute>(true);

        var isNoAttributeMapping = propertyName is null
            && jsonPropertyName is null
            && newtonsoftJsonProperty is null
            && dataMemberProperty is null
            && ignore is null
            && jsonIgnore is null
            && newtonsoftJsonIgnore is null;

        return isNoAttributeMapping
            ? null
            : new PropertyMapping {
                Name = propertyName?.Name ?? jsonPropertyName?.Name ?? newtonsoftJsonProperty?.PropertyName ?? dataMemberProperty?.Name,
                Ignore = ignore != null || jsonIgnore != null || newtonsoftJsonIgnore != null,
            };
    }
}
