using System.Text;
using System.Text.Json.Serialization.Metadata;
using Microsoft.JSInterop;
using static System.Console;

namespace ActualChat.App.AotHelper;

/// <summary>
/// Discovers which System.Text.Json internal converter types are needed for a set of root types.
/// Runs at generation time (JIT mode) where reflection works fully.
/// Outputs CodeKeeper.Keep(string) calls for use in Native AOT builds.
/// </summary>
public static class StjConverterDiscovery
{
    // Framework types that are always used by Blazor's JSRuntime IPC
    private static readonly Type[] FrameworkRootTypes = [
        typeof(JsonElement),
        typeof(JsonElement[]),
        typeof(Microsoft.AspNetCore.Components.ElementReference),
        typeof(Microsoft.AspNetCore.Components.NavigationOptions),
        // JSInterop enums serialized internally
        typeof(Microsoft.JSInterop.JSCallResultType),
        typeof(Microsoft.JSInterop.Infrastructure.JSCallType),
        // Blazor IPC uses these collection types
        typeof(IEnumerable<char>),
        typeof(KeyValuePair<string, object>),
        typeof(IEnumerable<KeyValuePair<string, object>>),
        typeof(ICollection<KeyValuePair<string, object>>),
        typeof(IReadOnlyCollection<KeyValuePair<string, object>>),
    ];

    // App types used in Blazor component parameters or JS interop that aren't
    // discoverable from [JSInvokable]/[JsonSerializable] scanning alone.
    // These need to be listed explicitly as additional root types.
    //
    // For dictionary-typed data that crosses JSRuntime.InvokeVoidAsync as `object`, STJ's
    // runtime-type dispatch resolves converters for the *interface* views too (IDictionary,
    // IReadOnlyDictionary, IEnumerable<KVP>, IReadOnlyCollection<KVP>, ICollection<KVP>), so
    // each interface variant has to be a root type for the graph walk to capture them.
    private static readonly Type[] AppRootTypes = [
        typeof(ActualChat.UI.Blazor.Components.VirtualListEdge),
        typeof(ActualChat.UI.Blazor.Services.Tune),
        typeof(ActualChat.UI.Blazor.Services.TuneInfo),
        typeof(Dictionary<ActualChat.UI.Blazor.Services.Tune, ActualChat.UI.Blazor.Services.TuneInfo>),
        typeof(IDictionary<ActualChat.UI.Blazor.Services.Tune, ActualChat.UI.Blazor.Services.TuneInfo>),
        typeof(IReadOnlyDictionary<ActualChat.UI.Blazor.Services.Tune, ActualChat.UI.Blazor.Services.TuneInfo>),
        typeof(IEnumerable<KeyValuePair<ActualChat.UI.Blazor.Services.Tune, ActualChat.UI.Blazor.Services.TuneInfo>>),
        typeof(IReadOnlyCollection<KeyValuePair<ActualChat.UI.Blazor.Services.Tune, ActualChat.UI.Blazor.Services.TuneInfo>>),
        typeof(ICollection<KeyValuePair<ActualChat.UI.Blazor.Services.Tune, ActualChat.UI.Blazor.Services.TuneInfo>>),
        typeof(KeyValuePair<ActualChat.UI.Blazor.Services.Tune, ActualChat.UI.Blazor.Services.TuneInfo>),
        // Dictionary<string, bool> family — surfaces in Blazor/JS interop under the `object`
        // runtime-type dispatch path (e.g. generic feature/flag maps). Same interface-variant
        // problem as Tune/TuneInfo above.
        typeof(Dictionary<string, bool>),
        typeof(IDictionary<string, bool>),
        typeof(IReadOnlyDictionary<string, bool>),
        typeof(IEnumerable<KeyValuePair<string, bool>>),
        typeof(IReadOnlyCollection<KeyValuePair<string, bool>>),
        typeof(ICollection<KeyValuePair<string, bool>>),
        typeof(KeyValuePair<string, bool>),
        typeof(List<ActualLab.Text.Symbol>),
        typeof(IEnumerable<ActualLab.Text.Symbol>),
        typeof(IReadOnlyCollection<ActualLab.Text.Symbol>),
        typeof(IReadOnlyList<ActualLab.Text.Symbol>),
        typeof(ICollection<ActualLab.Text.Symbol>),
        typeof(IList<ActualLab.Text.Symbol>),
        typeof(ActualLab.Text.Symbol),
        typeof(ActualChat.Chat.PlayableTextMarkup.Word),
        typeof(ActualChat.Chat.PlayableTextMarkup.Word[]),
    ];

    // Internal framework types (by AQN) that need keeping but can't be discovered via STJ
    private static readonly string[] FrameworkKeeps = [
        // JSInterop internals — TaskResultGetter<T> resolves JS return values going into
        // Task<T>/ValueTask<T>; TcsResultSetter<T> resolves managed-side Task<T> completion
        // when JS invokes .NET. Both are constructed reflectively via Activator.CreateInstance.
        "Microsoft.JSInterop.Infrastructure.TaskGenericsUtil+TaskResultGetter`1" +
            "[[System.Threading.Tasks.VoidTaskResult, System.Private.CoreLib]], Microsoft.JSInterop",
        "Microsoft.JSInterop.Infrastructure.TaskGenericsUtil+TcsResultSetter`1" +
            "[[System.Boolean, System.Private.CoreLib]], Microsoft.JSInterop",
        "Microsoft.JSInterop.Infrastructure.TaskGenericsUtil+TcsResultSetter`1" +
            "[[System.String, System.Private.CoreLib]], Microsoft.JSInterop",
        "Microsoft.JSInterop.Infrastructure.TaskGenericsUtil+TcsResultSetter`1" +
            "[[System.Int32, System.Private.CoreLib]], Microsoft.JSInterop",
        "Microsoft.JSInterop.Infrastructure.TaskGenericsUtil+TcsResultSetter`1" +
            "[[System.Threading.Tasks.VoidTaskResult, System.Private.CoreLib]], Microsoft.JSInterop",
        "System.Text.Json.Serialization.Converters.ObjectDefaultConverter`1" +
            "[[System.Threading.Tasks.VoidTaskResult, System.Private.CoreLib]], System.Text.Json",
        // JSRuntime base class (for reflection access to JsonSerializerOptions)
        "Microsoft.JSInterop.JSRuntime, Microsoft.JSInterop",
        // MAUI platform type
        "Microsoft.Maui.Controls.Compatibility.Platform.UWP.WindowsResourcesProvider, Microsoft.Maui.Controls",
    ];

    private static readonly string[] AssembliesToScan = [
        "ActualChat.UI.Blazor",
        "ActualChat.UI.Blazor.App",
    ];

    private static readonly JsonSerializerOptions Options = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    private static readonly HashSet<Type> Visited = new();
    private static readonly HashSet<string> ConverterAqns = new();

    /// <summary>
    /// Discovers root types from JS interop patterns in loaded assemblies,
    /// walks the STJ converter graph, and returns all needed AQNs.
    /// </summary>
    public static SortedSet<string> DiscoverAll()
    {
        var rootTypes = new HashSet<Type>();

        // 1. Framework + app root types
        foreach (var t in FrameworkRootTypes)
            rootTypes.Add(t);
        foreach (var t in AppRootTypes)
            rootTypes.Add(t);

        // 2. Scan [JSInvokable] method parameters and return types
        foreach (var type in ScanJSInvokableTypes())
            rootTypes.Add(type);

        // 3. Scan InvokeAsync<T> return types (from method signatures)
        foreach (var type in ScanInvokeAsyncReturnTypes())
            rootTypes.Add(type);

        WriteLine($"  Root types discovered: {rootTypes.Count}");
        foreach (var t in rootTypes.OrderBy(t => t.FullName))
            WriteLine($"    {t.FullName}");

        // 4. Walk STJ converter graph for each root type
        Visited.Clear();
        ConverterAqns.Clear();

        foreach (var type in rootTypes)
            WalkType(type);

        // 5. Add framework keeps
        foreach (var aqn in FrameworkKeeps)
            ConverterAqns.Add(aqn);

        // 6. Add collection interface converters for array/list element types
        // STJ resolves these lazily, so we need to pre-discover them
        var elementTypes = new HashSet<Type>();
        foreach (var type in rootTypes) {
            CollectElementTypes(type, elementTypes);
        }
        foreach (var elemType in elementTypes)
            AddCollectionConverters(elemType);

        return new SortedSet<string>(ConverterAqns, StringComparer.Ordinal);
    }

    private static void WalkType(Type type)
    {
        if (type == typeof(void) || type == typeof(object) || type == typeof(string))
            return;
        if (type.IsPrimitive && type != typeof(long) && type != typeof(float)
            && type != typeof(double) && type != typeof(int) && type != typeof(bool))
            return;
        if (!Visited.Add(type))
            return;

        try {
            var typeInfo = Options.GetTypeInfo(type);
            var converter = typeInfo.Converter;
            var converterType = converter.GetType();

            // Only keep internal STJ converters
            var converterAsm = converterType.Assembly.GetName().Name ?? "";
            if (converterAsm == "System.Text.Json") {
                var aqn = converterType.AssemblyQualifiedName;
                if (aqn != null)
                    ConverterAqns.Add(aqn);
            }

            // JsonPropertyInfo<T> for each property
            foreach (var prop in typeInfo.Properties) {
                var propInfoType = prop.GetType();
                if (propInfoType.IsGenericType) {
                    var propAqn = propInfoType.AssemblyQualifiedName;
                    if (propAqn != null && propInfoType.Assembly.GetName().Name == "System.Text.Json")
                        ConverterAqns.Add(propAqn);
                }
                WalkType(prop.PropertyType);
            }

            // Recurse into generic args (covers collections, dictionaries, etc.)
            if (type.IsArray)
                WalkType(type.GetElementType()!);
            else if (type.IsGenericType) {
                foreach (var arg in type.GetGenericArguments())
                    WalkType(arg);
            }
        }
        catch (Exception e) {
            WriteLine($"    WARN: {type.FullName}: {e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>
    /// For a given element type T, adds all collection interface converter variants:
    /// IEnumerable&lt;T&gt;, IReadOnlyCollection&lt;T&gt;, IReadOnlyList&lt;T&gt;,
    /// IList&lt;T&gt;, ICollection&lt;T&gt;, List&lt;T&gt;
    /// </summary>
    private static void AddCollectionConverters(Type elemType)
    {
        var collectionTypes = new[] {
            typeof(IEnumerable<>).MakeGenericType(elemType),
            typeof(IReadOnlyCollection<>).MakeGenericType(elemType),
            typeof(IReadOnlyList<>).MakeGenericType(elemType),
            typeof(IList<>).MakeGenericType(elemType),
            typeof(ICollection<>).MakeGenericType(elemType),
            typeof(List<>).MakeGenericType(elemType),
        };

        foreach (var collType in collectionTypes) {
            try {
                var typeInfo = Options.GetTypeInfo(collType);
                var converterType = typeInfo.Converter.GetType();
                if (converterType.Assembly.GetName().Name == "System.Text.Json") {
                    var aqn = converterType.AssemblyQualifiedName;
                    if (aqn != null)
                        ConverterAqns.Add(aqn);
                }
            }
            catch { /* Some types may not be resolvable */ }
        }

        // Also add dictionary interfaces if elemType is a KVP
        if (elemType.IsGenericType && elemType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>)) {
            var kvpArgs = elemType.GetGenericArguments();
            var dictTypes = new[] {
                typeof(IDictionary<,>).MakeGenericType(kvpArgs),
                typeof(IReadOnlyDictionary<,>).MakeGenericType(kvpArgs),
                typeof(Dictionary<,>).MakeGenericType(kvpArgs),
            };
            foreach (var dictType in dictTypes) {
                try {
                    var typeInfo = Options.GetTypeInfo(dictType);
                    var converterType = typeInfo.Converter.GetType();
                    if (converterType.Assembly.GetName().Name == "System.Text.Json") {
                        var aqn = converterType.AssemblyQualifiedName;
                        if (aqn != null)
                            ConverterAqns.Add(aqn);
                    }
                }
                catch { /* Some types may not be resolvable */ }
            }
        }
    }

    /// <summary>
    /// Collects element types from arrays, lists, dictionaries found in root types' properties.
    /// </summary>
    private static void CollectElementTypes(Type type, HashSet<Type> result)
    {
        if (type == typeof(string) || type.IsPrimitive || type == typeof(object))
            return;

        if (type.IsArray) {
            var elem = type.GetElementType()!;
            if (elem != typeof(object) && elem != typeof(string) && !elem.IsPrimitive)
                result.Add(elem);
            return;
        }

        // Walk properties to find collection/array types
        try {
            var typeInfo = Options.GetTypeInfo(type);
            foreach (var prop in typeInfo.Properties) {
                var pt = prop.PropertyType;
                if (pt.IsArray && pt.GetElementType() is { } elemType) {
                    if (elemType != typeof(object) && elemType != typeof(string) && !elemType.IsPrimitive)
                        result.Add(elemType);
                }
                if (pt.IsGenericType) {
                    var gtd = pt.GetGenericTypeDefinition();
                    if (gtd == typeof(List<>) || gtd == typeof(IList<>) || gtd == typeof(IReadOnlyList<>)
                        || gtd == typeof(IEnumerable<>) || gtd == typeof(ICollection<>)
                        || gtd == typeof(IReadOnlyCollection<>)) {
                        var elem = pt.GetGenericArguments()[0];
                        if (elem != typeof(object) && elem != typeof(string) && !elem.IsPrimitive)
                            result.Add(elem);
                    }
                    if (gtd == typeof(Dictionary<,>) || gtd == typeof(IDictionary<,>)
                        || gtd == typeof(IReadOnlyDictionary<,>)) {
                        var kvpType = typeof(KeyValuePair<,>).MakeGenericType(pt.GetGenericArguments());
                        result.Add(kvpType);
                    }
                }
            }
        }
        catch { /* Ignore types that can't be resolved */ }
    }

    /// <summary>
    /// Scans [JSInvokable] methods for parameter and return types.
    /// </summary>
    private static HashSet<Type> ScanJSInvokableTypes()
    {
        var result = new HashSet<Type>();
        var jsInvokableAttr = typeof(JSInvokableAttribute);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            var asmName = assembly.GetName().Name ?? "";
            if (!AssembliesToScan.Any(n => asmName == n))
                continue;

            try {
                foreach (var type in assembly.GetTypes()) {
                    foreach (var method in type.GetMethods(
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)) {
                        if (!method.IsDefined(jsInvokableAttr, true))
                            continue;

                        // Parameter types
                        foreach (var param in method.GetParameters()) {
                            var pt = param.ParameterType;
                            if (IsInterestingType(pt))
                                result.Add(pt);
                        }

                        // Return type (unwrap Task<T>)
                        var returnType = method.ReturnType;
                        if (returnType.IsGenericType) {
                            var gtd = returnType.GetGenericTypeDefinition();
                            if (gtd == typeof(Task<>) || gtd == typeof(ValueTask<>))
                                returnType = returnType.GetGenericArguments()[0];
                        }
                        if (IsInterestingType(returnType))
                            result.Add(returnType);
                    }
                }
            }
            catch (ReflectionTypeLoadException) { }
        }
        return result;
    }

    /// <summary>
    /// Scans for types used as generic arguments in InvokeAsync&lt;T&gt; calls.
    /// Since we can't easily scan IL, we scan for types that are:
    /// - Returned from IJSObjectReference/IJSRuntime InvokeAsync in our assemblies
    /// - Listed in our JsonSerializerContext attributes
    /// We discover these by finding all [JsonSerializable(typeof(T))] attributes.
    /// </summary>
    private static HashSet<Type> ScanInvokeAsyncReturnTypes()
    {
        var result = new HashSet<Type>();
        var jsonSerializableAttr = typeof(JsonSerializableAttribute);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            var asmName = assembly.GetName().Name ?? "";
            if (!AssembliesToScan.Any(n => asmName == n))
                continue;

            try {
                foreach (var type in assembly.GetTypes()) {
                    if (!typeof(JsonSerializerContext).IsAssignableFrom(type))
                        continue;

                    foreach (var attr in type.GetCustomAttributes(jsonSerializableAttr, false)) {
                        if (attr is JsonSerializableAttribute jsa && jsa.TypeInfoPropertyName != null) {
                            // Can't easily get the Type from the attribute via reflection in all cases
                        }
                    }

                    // Use the custom attributes data to get the type argument
                    foreach (var attrData in type.CustomAttributes) {
                        if (attrData.AttributeType != jsonSerializableAttr)
                            continue;
                        if (attrData.ConstructorArguments.Count > 0
                            && attrData.ConstructorArguments[0].Value is Type t) {
                            if (IsInterestingType(t))
                                result.Add(t);
                        }
                    }
                }
            }
            catch (ReflectionTypeLoadException) { }
        }
        return result;
    }

    private static bool IsInterestingType(Type type)
    {
        if (type == typeof(void) || type == typeof(object) || type == typeof(string))
            return false;
        if (type == typeof(bool) || type == typeof(int) || type == typeof(long)
            || type == typeof(double) || type == typeof(float) || type == typeof(byte))
            return false;
        if (type == typeof(IJSObjectReference) || type == typeof(IJSStreamReference))
            return false;
        if (type.IsGenericType) {
            var gtd = type.GetGenericTypeDefinition();
            if (gtd == typeof(Task<>) || gtd == typeof(ValueTask<>)
                || gtd == typeof(DotNetObjectReference<>))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Generates a C# file with CodeKeeper.Keep(string) calls for all discovered converter types.
    /// </summary>
    public static string GenerateKeepsFile(SortedSet<string> converterAqns)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("// Generated by App.AotHelper -s. Do not edit manually.");
        sb.AppendLine("// Contains CodeKeeper.Keep(string) calls for STJ internal converter types");
        sb.AppendLine("// needed for Native AOT JS interop compatibility.");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine();
        sb.AppendLine("namespace ActualChat.UI.Blazor.Module;");
        sb.AppendLine();
        sb.AppendLine("internal static partial class BlazorUIModuleInitializer");
        sb.AppendLine("{");
        sb.AppendLine("    static void KeepStjConverters()");
        sb.AppendLine("    {");
        sb.AppendLine("        if (CodeKeeper.AlwaysTrue)");
        sb.AppendLine("            return;");
        sb.AppendLine();

        foreach (var aqn in converterAqns) {
            sb.AppendLine($"        CodeKeeper.Keep(\"{EscapeString(aqn)}\");");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string EscapeString(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
