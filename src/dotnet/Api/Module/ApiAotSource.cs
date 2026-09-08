using ActualChat.Aot;
using ActualChat.Chat;
using ActualChat.Serialization.Internal;

namespace ActualChat.Module;

internal partial class ApiAotSource
{
    static ApiAotSource()
    {
        if (CodeKeeper.AlwaysTrue)
            return;

        // Reached only via AppMessagePackResolverSettings.Formatters, which the generator can't
        // see - ApiModuleInitializer registers these at runtime. Same reason CoreAotSource keeps
        // Size2DMessagePackFormatter.
        CodeKeeper.Keep<ForwardCompatibleUnionFormatter<ChatEntry>>();
        CodeKeeper.Keep<ForwardCompatibleUnionFormatter<SystemEntry>>();
        CodeKeeper.Keep<ForwardCompatibleUnionFormatter<Markup>>();
        CodeKeeper.Keep<ForwardCompatibleUnionFormatter<Notifications.Notification>>();
        CodeKeeper.Keep<ForwardCompatibleUnionFormatter<Invite.Invite>>();
    }
}
