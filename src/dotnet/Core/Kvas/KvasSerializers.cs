namespace ActualChat.Kvas;

public static class KvasSerializers
{
    public static ITextSerializer ValueTupleSerializer { get; set; } =
        new SystemJsonSerializer(new JsonSerializerOptions {
            IncludeFields = true,
            PropertyNamingPolicy = SystemJsonSerializer.DefaultOptions.PropertyNamingPolicy
        });
}

public static class KvasSerializers<T>
{
    public static ITextSerializer<T> ValueTupleSerializer { get; set; } =
        KvasSerializers.ValueTupleSerializer.ToTyped<T>();
}
