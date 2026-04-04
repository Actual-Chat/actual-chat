using ActualChat.Aot;
using Microsoft.JSInterop;

namespace ActualChat.UI.Blazor.Module;

#pragma warning disable CA2255 // Module initializer is intended to be used in...

internal static class BlazorUIModuleInitializer
{
    [ModuleInitializer]
    internal static void ModuleInitializer()
    {
        AotTypes.AddSource(new BlazorUIAotSource());
        AotJsonContexts.Add(BlazorUIJsonContext.Default);

        // Keep BlazorWebView IPC JSON serialization types for Native AOT.
        // These internal STJ converters are instantiated via reflection by the default resolver.
        // Type erasure: ref-type args share via __Canon, so we use object where possible.
        if (CodeKeeper.AlwaysFalse) {
            // Retain InjectJsonTypeInfoResolvers and the JSRuntime type it reflects on
            default(IJSRuntime)!.InjectJsonTypeInfoResolvers();
            CodeKeeper.Keep("Microsoft.JSInterop.JSRuntime, Microsoft.JSInterop");
        }
        KeepBlazorIpcTypes();
    }

    private static void KeepBlazorIpcTypes()
    {
        // Assemblies
        const string aCoreLib = "System.Private.CoreLib";
        const string aJson = "System.Text.Json";
        const string aRuntime = "System.Runtime";
        const string aJSInterop = "Microsoft.JSInterop";

        // Converter open generic type names
        const string tArrayConverter = "System.Text.Json.Serialization.Converters.ArrayConverter`2";
        const string tEnumConverter = "System.Text.Json.Serialization.Converters.EnumConverter`1";
        const string tObjectDefaultConverter = "System.Text.Json.Serialization.Converters.ObjectDefaultConverter`1";
        const string tIEnumerableOfTConverter = "System.Text.Json.Serialization.Converters.IEnumerableOfTConverter`2";
        const string tICollectionOfTConverter = "System.Text.Json.Serialization.Converters.ICollectionOfTConverter`2";
        const string tKvp = "System.Collections.Generic.KeyValuePair`2";

        // Assembly-qualified type refs
        const string qObject = "System.Object, " + aCoreLib;
        const string qChar = "System.Char, " + aCoreLib;
        const string qJsonElement = "System.Text.Json.JsonElement, " + aJson;
        const string qJSCallResultType = "Microsoft.JSInterop.JSCallResultType, " + aJSInterop;
        const string qJSCallType = "Microsoft.JSInterop.Infrastructure.JSCallType, " + aJSInterop;

        // Additional converter types
        const string tSmallObjectConverter = "System.Text.Json.Serialization.Converters.SmallObjectWithParameterizedConstructorConverter`5";

        // Assembly-qualified Blazor types
        const string aComponents = "Microsoft.AspNetCore.Components";
        const string aComponentsWeb = "Microsoft.AspNetCore.Components.Web";
        const string qElementReference = "Microsoft.AspNetCore.Components.ElementReference, " + aComponents;
        const string qString = "System.String, " + aCoreLib;
        const string qJSComponentParam = "Microsoft.AspNetCore.Components.Web.JSComponentConfigurationStore+JSComponentParameter[], " + aComponentsWeb;
        const string qListOfString = "System.Collections.Generic.List`1[[" + qString + "]], " + aCoreLib;

        // Composed types
        const string qKvp = tKvp + "[[" + qObject + "],[" + qObject + "]], " + aCoreLib;
        const string qKvpStringParam = tKvp + "[[" + qString + "],[" + qJSComponentParam + "]], " + aCoreLib;
        const string qKvpStringList = tKvp + "[[" + qString + "],[" + qListOfString + "]], " + aCoreLib;
        const string qIEnumerableOfChar = "System.Collections.Generic.IEnumerable`1[[" + qChar + "]], " + aRuntime;
        const string qIEnumerableOfKvp = "System.Collections.Generic.IEnumerable`1[[" + qKvp + "]], " + aRuntime;
        const string qICollectionOfKvp = "System.Collections.Generic.ICollection`1[[" + qKvp + "]], " + aRuntime;

        // App-specific assembly-qualified types
        const string aUIBlazor = "ActualChat.UI.Blazor";
        const string qSideNavSide = "ActualChat.UI.Blazor.Components.SideNav.SideNavSide, " + aUIBlazor;
        const string qVirtualListEdge = "ActualChat.UI.Blazor.Components.VirtualListEdge, " + aUIBlazor;
        const string qTune = "ActualChat.UI.Blazor.Services.Tune, " + aUIBlazor;
        const string qTuneInfo = "ActualChat.UI.Blazor.Services.TuneInfo, " + aUIBlazor;
        const string qKvpTuneTuneInfo = tKvp + "[[" + qTune + "],[" + qTuneInfo + "]], " + aCoreLib;

        const string qInt32 = "System.Int32, " + aCoreLib;

        CodeKeeper.Keep<JsonElement>();
        // ArrayConverters
        CodeKeeper.Keep(tArrayConverter + "[[" + qObject + "],[" + qJsonElement + "]], " + aJson);
        CodeKeeper.Keep(tArrayConverter + "[[" + qObject + "],[" + qInt32 + "]], " + aJson);
        // EnumConverters
        CodeKeeper.Keep(tEnumConverter + "[[" + qJSCallResultType + "]], " + aJson);
        CodeKeeper.Keep(tEnumConverter + "[[" + qJSCallType + "]], " + aJson);
        CodeKeeper.Keep(tEnumConverter + "[[" + qSideNavSide + "]], " + aJson);
        CodeKeeper.Keep(tEnumConverter + "[[" + qVirtualListEdge + "]], " + aJson);
        CodeKeeper.Keep(tEnumConverter + "[[" + qTune + "]], " + aJson);
        // ObjectDefaultConverter<KVP<object, object>>, ObjectDefaultConverter<ElementReference>
        CodeKeeper.Keep(tObjectDefaultConverter + "[[" + qKvp + "]], " + aJson);
        CodeKeeper.Keep(tObjectDefaultConverter + "[[" + qElementReference + "]], " + aJson);

        // SmallObjectWithParameterizedConstructorConverter for KVP<string, JSComponentParameter[]>
        CodeKeeper.Keep(tSmallObjectConverter + "[[" + qKvpStringParam + "],[" + qString + "],[" + qJSComponentParam + "],[" + qObject + "],[" + qObject + "]], " + aJson);
        // SmallObjectWithParameterizedConstructorConverter for KVP<string, List<string>>
        CodeKeeper.Keep(tSmallObjectConverter + "[[" + qKvpStringList + "],[" + qString + "],[" + qListOfString + "],[" + qObject + "],[" + qObject + "]], " + aJson);
        // SmallObjectWithParameterizedConstructorConverter for KVP<Tune, TuneInfo>
        CodeKeeper.Keep(tSmallObjectConverter + "[[" + qKvpTuneTuneInfo + "],[" + qTune + "],[" + qTuneInfo + "],[" + qObject + "],[" + qObject + "]], " + aJson);
        // JsonPropertyInfo<Tune> — property-level metadata for Tune enum
        CodeKeeper.Keep("System.Text.Json.Serialization.Metadata.JsonPropertyInfo`1[[" + qTune + "]], " + aJson);
        // DictionaryOfTKeyTValueConverter for Dictionary<Tune, TuneInfo>
        const string tDictConverter = "System.Text.Json.Serialization.Converters.DictionaryOfTKeyTValueConverter`3";
        const string qDictTuneTuneInfo = "System.Collections.Generic.Dictionary`2[[" + qTune + "],[" + qTuneInfo + "]], " + aCoreLib;
        CodeKeeper.Keep(tDictConverter + "[[" + qDictTuneTuneInfo + "],[" + qTune + "],[" + qTuneInfo + "]], " + aJson);
        // SmallObjectWithParameterizedConstructorConverter for VirtualListDataQuery
        const string aActualChatCore = "ActualChat.Core";
        const string qDouble = "System.Double, " + aCoreLib;
        const string qRangeString = "ActualChat.Mathematics.Range`1[[" + qString + "]], " + aActualChatCore;
        const string qRangeDouble = "ActualChat.Mathematics.Range`1[[" + qDouble + "]], " + aActualChatCore;
        const string qRangeInt = "ActualChat.Mathematics.Range`1[[" + qInt32 + "]], " + aActualChatCore;
        const string qVirtualListDataQuery = "ActualChat.UI.Blazor.Components.VirtualListDataQuery, " + aUIBlazor;
        CodeKeeper.Keep(tSmallObjectConverter + "[[" + qVirtualListDataQuery + "],[" + qRangeString + "],[" + qRangeDouble + "],[" + qRangeInt + "],[" + qObject + "]], " + aJson);
        // Range<T> — SmallObjectConverter + JsonPropertyInfo for all used variants
        const string qInt64 = "System.Int64, " + aCoreLib;
        const string qSingle = "System.Single, " + aCoreLib;
        const string qRangeLong = "ActualChat.Mathematics.Range`1[[" + qInt64 + "]], " + aActualChatCore;
        const string qRangeFloat = "ActualChat.Mathematics.Range`1[[" + qSingle + "]], " + aActualChatCore;
        const string tJsonPropertyInfo = "System.Text.Json.Serialization.Metadata.JsonPropertyInfo`1";
        // SmallObjectConverters
        CodeKeeper.Keep(tSmallObjectConverter + "[[" + qRangeString + "],[" + qString + "],[" + qString + "],[" + qObject + "],[" + qObject + "]], " + aJson);
        CodeKeeper.Keep(tSmallObjectConverter + "[[" + qRangeDouble + "],[" + qDouble + "],[" + qDouble + "],[" + qObject + "],[" + qObject + "]], " + aJson);
        CodeKeeper.Keep(tSmallObjectConverter + "[[" + qRangeInt + "],[" + qInt32 + "],[" + qInt32 + "],[" + qObject + "],[" + qObject + "]], " + aJson);
        CodeKeeper.Keep(tSmallObjectConverter + "[[" + qRangeLong + "],[" + qInt64 + "],[" + qInt64 + "],[" + qObject + "],[" + qObject + "]], " + aJson);
        CodeKeeper.Keep(tSmallObjectConverter + "[[" + qRangeFloat + "],[" + qSingle + "],[" + qSingle + "],[" + qObject + "],[" + qObject + "]], " + aJson);
        // JsonPropertyInfo for each Range variant
        CodeKeeper.Keep(tJsonPropertyInfo + "[[" + qRangeString + "]], " + aJson);
        CodeKeeper.Keep(tJsonPropertyInfo + "[[" + qRangeDouble + "]], " + aJson);
        CodeKeeper.Keep(tJsonPropertyInfo + "[[" + qRangeInt + "]], " + aJson);
        CodeKeeper.Keep(tJsonPropertyInfo + "[[" + qRangeLong + "]], " + aJson);
        CodeKeeper.Keep(tJsonPropertyInfo + "[[" + qRangeFloat + "]], " + aJson);

        // ObjectDefaultConverter<NavigationOptions> — Blazor navigation
        const string qNavigationOptions = "Microsoft.AspNetCore.Components.NavigationOptions, " + aComponents;
        CodeKeeper.Keep(tObjectDefaultConverter + "[[" + qNavigationOptions + "]], " + aJson);

        // JSInterop internal: TaskResultGetter<VoidTaskResult>
        CodeKeeper.Keep("Microsoft.JSInterop.Infrastructure.TaskGenericsUtil+TaskResultGetter`1" +
            "[[System.Threading.Tasks.VoidTaskResult, " + aCoreLib + "]], " + aJSInterop);

        // Value types needing RuntimeHelpers.IsReferenceOrContainsReferences<T>
        CodeKeeper.Keep<long>();
        CodeKeeper.Keep<float>();
        // IEnumerableOfTConverter, ICollectionOfTConverter (for object KVPs)
        CodeKeeper.Keep(tIEnumerableOfTConverter + "[[" + qIEnumerableOfChar + "],[" + qChar + "]], " + aJson);
        CodeKeeper.Keep(tIEnumerableOfTConverter + "[[" + qIEnumerableOfKvp + "],[" + qKvp + "]], " + aJson);
        CodeKeeper.Keep(tICollectionOfTConverter + "[[" + qICollectionOfKvp + "],[" + qKvp + "]], " + aJson);

        // Dictionary<Tune,TuneInfo> interface converters
        const string tIDictConverter = "System.Text.Json.Serialization.Converters.IDictionaryOfTKeyTValueConverter`3";
        const string tIReadOnlyDictConverter = "System.Text.Json.Serialization.Converters.IReadOnlyDictionaryOfTKeyTValueConverter`3";
        const string qIDictTuneTuneInfo = "System.Collections.Generic.IDictionary`2[[" + qTune + "],[" + qTuneInfo + "]], " + aRuntime;
        const string qIReadOnlyDictTuneTuneInfo = "System.Collections.Generic.IReadOnlyDictionary`2[[" + qTune + "],[" + qTuneInfo + "]], " + aRuntime;
        const string qIEnumerableOfKvpTune = "System.Collections.Generic.IEnumerable`1[[" + qKvpTuneTuneInfo + "]], " + aRuntime;
        const string qIReadOnlyCollectionOfKvpTune = "System.Collections.Generic.IReadOnlyCollection`1[[" + qKvpTuneTuneInfo + "]], " + aRuntime;
        const string qICollectionOfKvpTune = "System.Collections.Generic.ICollection`1[[" + qKvpTuneTuneInfo + "]], " + aRuntime;
        CodeKeeper.Keep(tIDictConverter + "[[" + qIDictTuneTuneInfo + "],[" + qTune + "],[" + qTuneInfo + "]], " + aJson);
        CodeKeeper.Keep(tIReadOnlyDictConverter + "[[" + qIReadOnlyDictTuneTuneInfo + "],[" + qTune + "],[" + qTuneInfo + "]], " + aJson);
        CodeKeeper.Keep(tIEnumerableOfTConverter + "[[" + qIEnumerableOfKvpTune + "],[" + qKvpTuneTuneInfo + "]], " + aJson);
        CodeKeeper.Keep(tIEnumerableOfTConverter + "[[" + qIReadOnlyCollectionOfKvpTune + "],[" + qKvpTuneTuneInfo + "]], " + aJson);
        CodeKeeper.Keep(tICollectionOfTConverter + "[[" + qICollectionOfKvpTune + "],[" + qKvpTuneTuneInfo + "]], " + aJson);

        // PlayableTextMarkup.Word — array + object converters
        const string aApi = "ActualChat.Api";
        const string qPlayableWord = "ActualChat.Chat.PlayableTextMarkup+Word, " + aApi;
        CodeKeeper.Keep(tArrayConverter + "[[" + qObject + "],[" + qPlayableWord + "]], " + aJson);
        CodeKeeper.Keep(tObjectDefaultConverter + "[[" + qPlayableWord + "]], " + aJson);

        // ObjectDefaultConverter<VoidTaskResult>
        const string qVoidTaskResult = "System.Threading.Tasks.VoidTaskResult, " + aCoreLib;
        CodeKeeper.Keep(tObjectDefaultConverter + "[[" + qVoidTaskResult + "]], " + aJson);

        // Dictionary<string, bool> converters (used in Blazor component parameters)
        const string qBool = "System.Boolean, " + aCoreLib;
        const string qKvpStringBool = tKvp + "[[" + qString + "],[" + qBool + "]], " + aCoreLib;
        const string qIDictStringBool = "System.Collections.Generic.IDictionary`2[[" + qString + "],[" + qBool + "]], " + aRuntime;
        const string qIReadOnlyDictStringBool = "System.Collections.Generic.IReadOnlyDictionary`2[[" + qString + "],[" + qBool + "]], " + aRuntime;
        const string qIEnumerableOfKvpStringBool = "System.Collections.Generic.IEnumerable`1[[" + qKvpStringBool + "]], " + aRuntime;
        const string qIReadOnlyCollOfKvpStringBool = "System.Collections.Generic.IReadOnlyCollection`1[[" + qKvpStringBool + "]], " + aRuntime;
        const string qICollOfKvpStringBool = "System.Collections.Generic.ICollection`1[[" + qKvpStringBool + "]], " + aRuntime;
        CodeKeeper.Keep(tIDictConverter + "[[" + qIDictStringBool + "],[" + qString + "],[" + qBool + "]], " + aJson);
        CodeKeeper.Keep(tIReadOnlyDictConverter + "[[" + qIReadOnlyDictStringBool + "],[" + qString + "],[" + qBool + "]], " + aJson);
        CodeKeeper.Keep(tIEnumerableOfTConverter + "[[" + qIEnumerableOfKvpStringBool + "],[" + qKvpStringBool + "]], " + aJson);
        CodeKeeper.Keep(tIEnumerableOfTConverter + "[[" + qIReadOnlyCollOfKvpStringBool + "],[" + qKvpStringBool + "]], " + aJson);
        CodeKeeper.Keep(tICollectionOfTConverter + "[[" + qICollOfKvpStringBool + "],[" + qKvpStringBool + "]], " + aJson);
        CodeKeeper.Keep(tSmallObjectConverter + "[[" + qKvpStringBool + "],[" + qString + "],[" + qBool + "],[" + qObject + "],[" + qObject + "]], " + aJson);

        // Fusion Blazor: ComputedStateComponent.CreateDefaultStateOptionsFactory<bool>
        CodeKeeper.Keep("ActualLab.Fusion.Blazor.ComputedStateComponent+CreateDefaultStateOptionsFactory`1" +
            "[[" + qBool + "]], ActualLab.Fusion.Blazor");

        // List<Symbol> and its interface converters
        const string tListConverter = "System.Text.Json.Serialization.Converters.ListOfTConverter`2";
        const string tIListConverter = "System.Text.Json.Serialization.Converters.IListOfTConverter`2";
        const string aActualLab = "ActualLab.Core";
        const string qSymbol = "ActualLab.Text.Symbol, " + aActualLab;
        const string qListOfSymbol = "System.Collections.Generic.List`1[[" + qSymbol + "]], " + aCoreLib;
        const string qIListOfSymbol = "System.Collections.Generic.IList`1[[" + qSymbol + "]], " + aRuntime;
        const string qICollectionOfSymbol = "System.Collections.Generic.ICollection`1[[" + qSymbol + "]], " + aRuntime;
        const string qIEnumerableOfSymbol = "System.Collections.Generic.IEnumerable`1[[" + qSymbol + "]], " + aRuntime;
        const string qIReadOnlyCollectionOfSymbol = "System.Collections.Generic.IReadOnlyCollection`1[[" + qSymbol + "]], " + aRuntime;
        const string qIReadOnlyListOfSymbol = "System.Collections.Generic.IReadOnlyList`1[[" + qSymbol + "]], " + aRuntime;
        CodeKeeper.Keep(tListConverter + "[[" + qListOfSymbol + "],[" + qSymbol + "]], " + aJson);
        CodeKeeper.Keep(tIListConverter + "[[" + qIListOfSymbol + "],[" + qSymbol + "]], " + aJson);
        CodeKeeper.Keep(tICollectionOfTConverter + "[[" + qICollectionOfSymbol + "],[" + qSymbol + "]], " + aJson);
        CodeKeeper.Keep(tIEnumerableOfTConverter + "[[" + qIEnumerableOfSymbol + "],[" + qSymbol + "]], " + aJson);
        CodeKeeper.Keep(tIEnumerableOfTConverter + "[[" + qIReadOnlyCollectionOfSymbol + "],[" + qSymbol + "]], " + aJson);
        CodeKeeper.Keep(tIEnumerableOfTConverter + "[[" + qIReadOnlyListOfSymbol + "],[" + qSymbol + "]], " + aJson);
    }
}
