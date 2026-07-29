using ActualChat.Diff.Handlers;
using ActualLab.Interception.Trimming;
using MessagePack.ImmutableCollection;

namespace ActualChat.UI.Blazor.App.Module;

// Manual CodeKeeper entries — not overwritten by the generator.
// The generated partial (BlazorUIAppAotSource.g.cs) handles discovered types.
internal partial class BlazorUIAppAotSource
{
    static BlazorUIAppAotSource()
    {
        if (CodeKeeper.AlwaysTrue)
            return;

        ProxyCodeKeeper.Extension = new AppProxyCodeKeeper();

        // Libraries
        CodeKeeper.Keep<PriorityQueue<object, object>>();
        CodeKeeper.Keep<Range<object>>();
        CodeKeeper.Keep<NewtonsoftJsonSerialized<object>>();

        // Blazor framework internals
        CodeKeeper.Keep<DotNetObjectReference<object>>();
        CodeKeeper.Keep<EventCallback<object>>();
        CodeKeeper.Keep<HeadOutlet>();
        CodeKeeper.Keep("Microsoft.JSInterop.Infrastructure.ArrayBuilder`1, Microsoft.JSInterop");
        CodeKeeper.Keep("Microsoft.JSInterop.Infrastructure.DotNetObjectReferenceJsonConverter`1, Microsoft.JSInterop");

        // Parameter comparers
        CodeKeeper.Keep<ParameterComparer>();
        CodeKeeper.Keep<ByValueParameterComparer>();
        CodeKeeper.Keep<ByItemParameterComparer>();
        CodeKeeper.Keep<ByItemSetParameterComparer>();
        CodeKeeper.Keep<ByNoneParameterComparer>();
        CodeKeeper.Keep<ByRefParameterComparer>();
        CodeKeeper.Keep<ByUuidParameterComparer>();
        CodeKeeper.Keep<DefaultParameterComparer>();
        CodeKeeper.Keep<ByVersionParameterComparer<long>>();
        CodeKeeper.Keep<ByIdAndVersionParameterComparer<ChatId, long>>();
        CodeKeeper.Keep<ByIdAndVersionParameterComparer<PlaceId, long>>();
        CodeKeeper.Keep<ByUuidAndVersionParameterComparer<long>>();

        // Diff handlers (use concrete types that satisfy constraints)
        CodeKeeper.Keep<MissingDiffHandler<Chat.Chat, ChatDiff>>();
        CodeKeeper.Keep<ObjectDiffHandler<string>>();
        CodeKeeper.Keep<StringDiffHandler>();
        CodeKeeper.Keep<NullableDiffHandler<int>>();
        CodeKeeper.Keep<RecordDiffHandler<Chat.Chat, ChatDiff>>();
        CodeKeeper.Keep<OptionDiffHandler<string>>();
        CodeKeeper.Keep<SetDiffHandler<ApiSet<AuthorId>, AuthorId>>();

        // Components & services
        CodeKeeper.Keep<TextInputOptions>();
        CodeKeeper.Keep<ChatView>();
        CodeKeeper.Keep<AudioRecorder>();
        CodeKeeper.Keep<AudioTrackPlayer>();
        CodeKeeper.Keep<NotificationUI>();
        CodeKeeper.Keep<InterfaceImmutableDictionaryFormatter<PlaceId, ChatId>>();
        CodeKeeper.Keep<InterfaceImmutableDictionaryFormatter<string, ChatId>>();
    }
}
