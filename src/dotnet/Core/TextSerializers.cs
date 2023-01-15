namespace ActualChat;

public static class TextSerializers
{
    public static ITextSerializer ValueTupleSerializer { get; set; } =
        new SystemJsonSerializer(new JsonSerializerOptions {
            IncludeFields = true,
            PropertyNamingPolicy = SystemJsonSerializer.DefaultOptions.PropertyNamingPolicy,
        });
}

public static class TextSerializers<T>
{
    public static ITextSerializer<T> ValueTupleSerializer { get; set; } =
        TextSerializers.ValueTupleSerializer.ToTyped<T>();
}
