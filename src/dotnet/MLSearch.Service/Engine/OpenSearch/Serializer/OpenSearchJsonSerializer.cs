using ActualChat.MLSearch.Engine.OpenSearch.Serializer.Converters;
using OpenSearch.Client;
using OpenSearch.Net;

namespace ActualChat.MLSearch.Engine.OpenSearch.Serializer;

internal sealed class OpenSearchJsonSerializer(
    IOpenSearchSerializer builtin,
    IConnectionSettingsValues settings,
    Func<JsonSerializerOptions>? jsonSerializerOptionsFactory = null
    ) : IOpenSearchSerializer, IPropertyMappingProvider
{
    public static IOpenSearchSerializer Default(IOpenSearchSerializer builtin, IConnectionSettingsValues values)
        => new OpenSearchJsonSerializer(builtin, values);

    private class CustomNamingPolicy(Func<string, string> nameInferer) : JsonNamingPolicy
    {
        public override string ConvertName(string name) => nameInferer.Invoke(name);
    }

    private (JsonSerializerOptions Compact, JsonSerializerOptions Indented)? _serializerOptions;
    private JsonSerializerOptions SerializerOptions => (_serializerOptions ??= CreateOptions()).Compact;
    private JsonSerializerOptions IndentedSerializerOptions => (_serializerOptions ??= CreateOptions()).Indented;

    private (JsonSerializerOptions, JsonSerializerOptions) CreateOptions()
    {
        var namingPolicy = settings.DefaultFieldNameInferrer is null
            ? null
            : new CustomNamingPolicy(settings.DefaultFieldNameInferrer);

        var typeResolver = new OpenSearchTypeInfoResolver(settings);

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

        return (Create(false), Create(true));

        JsonSerializerOptions Create(bool writeIndented)
        {
            var options = jsonSerializerOptionsFactory?.Invoke() ?? new JsonSerializerOptions() {
                AllowTrailingCommas = true,
            };
            options.TypeInfoResolver = typeResolver;
            options.Converters.AddRange(systemConverters);
            if (namingPolicy is not null) {
                options.PropertyNamingPolicy = namingPolicy;
            }
            options.WriteIndented = writeIndented;

            return options;
        }
    }

    public object Deserialize(Type type, Stream stream) => JsonSerializer.Deserialize(stream, type, SerializerOptions)!;

    public T Deserialize<T>(Stream stream) => JsonSerializer.Deserialize<T>(stream, SerializerOptions)!;

    public async Task<object> DeserializeAsync(Type type, Stream stream, CancellationToken cancellationToken = default)
        => (await JsonSerializer.DeserializeAsync(stream, type, SerializerOptions, cancellationToken).ConfigureAwait(false))!;

    public async Task<T> DeserializeAsync<T>(Stream stream, CancellationToken cancellationToken = default)
        => (await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false))!;

    public void Serialize<T>(T data, Stream stream, SerializationFormatting formatting = SerializationFormatting.None)
    {
        var options = formatting == SerializationFormatting.Indented ? IndentedSerializerOptions : SerializerOptions;
        JsonSerializer.Serialize(stream, data, options);
    }

    public async Task SerializeAsync<T>(T data, Stream stream, SerializationFormatting formatting = SerializationFormatting.None, CancellationToken cancellationToken = default)
    {
        var options = formatting == SerializationFormatting.Indented ? IndentedSerializerOptions : SerializerOptions;
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
