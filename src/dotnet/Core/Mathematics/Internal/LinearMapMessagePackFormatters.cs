using MessagePack.Formatters;

namespace ActualChat.Mathematics.Internal;

public sealed class LinearMapMessagePackFormatter : IMessagePackFormatter<LinearMap>
{
    public void Serialize(ref MessagePackWriter writer, LinearMap value, MessagePackSerializerOptions options)
    {
        writer.WriteMapHeader(1);
        writer.Write(nameof(LinearMap.Data));
        options.Resolver.GetFormatterWithVerify<float[]>().Serialize(ref writer, value.Data, options);
    }

    public LinearMap Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
            return default;
        var mapLen = reader.ReadMapHeader();
        float[] data = [];
        for (var i = 0; i < mapLen; i++) {
            var key = reader.ReadString();
            if (key == nameof(LinearMap.Data))
                data = options.Resolver.GetFormatterWithVerify<float[]>().Deserialize(ref reader, options) ?? [];
            else
                reader.Skip();
        }
        return new LinearMap(data);
    }
}

public sealed class LinearMapDiffMessagePackFormatter : IMessagePackFormatter<LinearMapDiff>
{
    public void Serialize(ref MessagePackWriter writer, LinearMapDiff value, MessagePackSerializerOptions options)
    {
        writer.WriteMapHeader(2);
        writer.Write(nameof(LinearMapDiff.Suffix));
        options.Resolver.GetFormatterWithVerify<LinearMap>().Serialize(ref writer, value.Suffix, options);
        writer.Write(nameof(LinearMapDiff.IsRewrite));
        writer.Write(value.IsRewrite);
    }

    public LinearMapDiff Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
            return default;
        var mapLen = reader.ReadMapHeader();
        LinearMap suffix = default;
        var isRewrite = false;
        for (var i = 0; i < mapLen; i++) {
            var key = reader.ReadString();
            switch (key) {
                case nameof(LinearMapDiff.Suffix):
                    suffix = options.Resolver.GetFormatterWithVerify<LinearMap>().Deserialize(ref reader, options);
                    break;
                case nameof(LinearMapDiff.IsRewrite):
                    isRewrite = reader.ReadBoolean();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
        return new LinearMapDiff(suffix, isRewrite);
    }
}

public sealed class OldLinearMapMessagePackFormatter : IMessagePackFormatter<OldLinearMap>
{
    public void Serialize(ref MessagePackWriter writer, OldLinearMap value, MessagePackSerializerOptions options)
    {
        writer.WriteMapHeader(2);
        writer.Write(nameof(OldLinearMap.SourcePoints));
        options.Resolver.GetFormatterWithVerify<float[]>().Serialize(ref writer, value.SourcePoints, options);
        writer.Write(nameof(OldLinearMap.TargetPoints));
        options.Resolver.GetFormatterWithVerify<float[]>().Serialize(ref writer, value.TargetPoints, options);
    }

    public OldLinearMap Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        if (reader.TryReadNil())
            return default;
        var mapLen = reader.ReadMapHeader();
        float[] source = [];
        float[] target = [];
        for (var i = 0; i < mapLen; i++) {
            var key = reader.ReadString();
            switch (key) {
                case nameof(OldLinearMap.SourcePoints):
                    source = options.Resolver.GetFormatterWithVerify<float[]>().Deserialize(ref reader, options) ?? [];
                    break;
                case nameof(OldLinearMap.TargetPoints):
                    target = options.Resolver.GetFormatterWithVerify<float[]>().Deserialize(ref reader, options) ?? [];
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
        return new OldLinearMap(source, target);
    }
}
