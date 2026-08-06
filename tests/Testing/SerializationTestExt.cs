namespace ActualChat.Testing;

// Mirrors ActualLab.Testing's serializer matrix minus MemoryPack. ActualLab's
// PassThroughTypeDecoratingByteSerializer hard-codes MemoryPack and ignores
// SerializationTestExt.UseMemoryPackSerializer, so the type-decorating leg is
// backed by MessagePack here instead.
public static class SerializationTestExt
{
    public static T AssertPassesThroughSerializers<T>(this T value, ITestOutputHelper? output = null)
        => PassThroughSerializerMatrix(value, v => v.Should().Be(value), output);

    public static T AssertPassesThroughSerializers<T>(this T value, Action<T> assertion, ITestOutputHelper? output = null)
        => PassThroughSerializerMatrix(value, assertion, output);

    public static T AssertPassesThroughSerializers<T>(this T value, Action<T, T> assertion, ITestOutputHelper? output = null)
        => PassThroughSerializerMatrix(value, v => assertion.Invoke(v, value), output);

    public static T PassThroughSerializers<T>(this T value, ITestOutputHelper? output = null)
        => PassThroughSerializerMatrix(value, static _ => { }, output);

    // Private methods

    private static T PassThroughSerializerMatrix<T>(T value, Action<T> assertion, ITestOutputHelper? output)
    {
        var v = value;
        v = v.PassThroughSystemJsonSerializer(output);
        assertion.Invoke(v);
        v = v.PassThroughNewtonsoftJsonSerializer(output);
        assertion.Invoke(v);
        v = v.PassThroughMessagePackByteSerializer(output);
        assertion.Invoke(v);
        v = v.PassThroughTypeDecoratingTextSerializer(output);
        assertion.Invoke(v);
        v = PassThroughTypeDecoratingMessagePackSerializer(v, output);
        assertion.Invoke(v);
        v = v.PassThroughUniSerialized(output);
        assertion.Invoke(v);
        v = v.PassThroughTypeDecoratingUniSerialized(output);
        assertion.Invoke(v);
        return v;
    }

    private static T PassThroughTypeDecoratingMessagePackSerializer<T>(T value, ITestOutputHelper? output)
    {
        var s = new TypeDecoratingByteSerializer(Serializers.MessagePack);
        using var buffer = s.Write(value, typeof(object));
        var data = buffer.WrittenMemory.ToArray();
        output?.WriteLine($"TypeDecoratingByteSerializer: {data.AsByteString()}");
        return (T)s.Read(data, typeof(object), out _)!;
    }
}
