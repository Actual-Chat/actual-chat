using ActualChat.Serialization.Internal;

namespace ActualChat.Module;

// Manual CodeKeeper entries — not overwritten by the generator.
internal partial class CoreAotSource
{
    static CoreAotSource()
    {
        if (CodeKeeper.AlwaysTrue)
            return;

        // Reached only via AppMessagePackResolver.Settings.ExtraFormatters, which the generator
        // can't see - it resolves formatters through MessagePack's own default resolver
        CodeKeeper.Keep<Size2DMessagePackFormatter>();
    }
}
