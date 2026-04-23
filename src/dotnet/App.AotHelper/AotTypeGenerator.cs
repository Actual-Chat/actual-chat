using System.Text;
using System.Text.RegularExpressions;
using ActualChat.Aot;
using ActualLab.Rpc;
using Microsoft.AspNetCore.Components;
using static System.Console;

namespace ActualChat.App.AotHelper;

public static class AotTypeGenerator
{
    /// <summary>
    /// If <c>true</c>, emitted <c>CodeKeeper.Keep("&lt;AQN&gt;")</c> calls use the full
    /// assembly-qualified name (including <c>Version=..., Culture=..., PublicKeyToken=...</c>
    /// for every referenced assembly). If <c>false</c> (default), the version/culture/token
    /// are stripped and each assembly reference becomes a simple name (e.g.
    /// <c>"Ns.Type, MyAssembly"</c>).
    /// <para>
    /// Short form is accepted by <see cref="Type.GetType(string)"/> and is picked up by
    /// ILC's trimmer dataflow analysis — it cuts the generated <c>*AotSource.g.cs</c> files
    /// down dramatically. The long form is only needed if two strongly-named assemblies
    /// share the same simple name, which isn't our situation.
    /// </para>
    /// </summary>
    public static readonly bool UseVersionedAqns = false;

    // Matches `, Version=..., Culture=..., PublicKeyToken=...` anywhere in an AQN, including
    // inside nested `[[...]]` generic-arg AQNs. The three properties always appear in this
    // order per the CLR AQN format.
    private static readonly Regex AsmQualifierRegex = new(
        @",\s*Version=[^,\]]+,\s*Culture=[^,\]]+,\s*PublicKeyToken=[^,\]]+",
        RegexOptions.Compiled);

    private static string NormalizeAqn(string aqn)
        => UseVersionedAqns ? aqn : AsmQualifierRegex.Replace(aqn, "");

    // Assemblies to scan for components, APIs, and serializable types. The witness + AotSource
    // files are emitted into each assembly's own project folder (see Targets below).
    //
    // Scope: AOT-trimmed client surface only. Server-side projects (Backend, Core.Server, all
    // Contracts/Services, App.Server) deliberately go through PolyType's reflection provider
    // at runtime — see Serializers.EnableReflection. Adding a server-only assembly here just
    // produces dead generated files; leave them out.
    private static readonly string[] AssemblyNames = [
        "ActualChat.Core",
        "ActualChat.Api",
        "ActualChat.Api.Contracts",
        "ActualChat.UI.Blazor",
        "ActualChat.UI.Blazor.App",
    ];

    // Framework types that must be kept for Native AOT (emitted into CoreAotSource).
    // ArraySegment<byte> / <char> are referenced by HttpContent, BufferedFileStreamStrategy,
    // MemoryMarshal.TryGetArray, etc., and ILC can drop their generic instantiations
    // when nothing in our code keeps them.
    private static readonly string[] FrameworkTypeKeeps = [
        "global::System.ArraySegment<byte>",
        "global::System.ArraySegment<char>",
    ];

    // Target projects + the PolyType witness class generated alongside each AotSource.
    // Entries with a non-null AotSourceRelativePath also get an IAotSource (for AOT/trimming
    // retention); witness-only entries emit just the witness + its self-registering
    // ModuleInitializer, suitable for server-side contracts/services that don't need
    // explicit AOT retention.
    private static readonly AotSourceTarget[] Targets = BuildTargets();

    private static AotSourceTarget[] BuildTargets()
    {
        var explicitTargets = new AotSourceTarget[] {
            new("ActualChat.Core",
                "CoreAotSource",
                "CoreWitness",
                "ActualChat.Module",
                "Core/Module/CoreAotSource.g.cs",
                "Core/Module/CoreModuleInitializer.g.cs",
                [AotTypeKind.Api, AotTypeKind.Serializable]),
            new("ActualChat.Api",
                "ApiAotSource",
                "ApiWitness",
                "ActualChat.Module",
                "Api/Module/ApiAotSource.g.cs",
                "Api/Module/ApiModuleInitializer.g.cs",
                [AotTypeKind.Serializable]),
            new("ActualChat.Api.Contracts",
                "ApiContractsAotSource",
                "ApiContractsWitness",
                "ActualChat.Module",
                "Api.Contracts/Module/ApiContractsAotSource.g.cs",
                "Api.Contracts/Module/ApiContractsModuleInitializer.g.cs",
                [AotTypeKind.Api, AotTypeKind.Serializable]),
            new("ActualChat.UI.Blazor",
                "BlazorUIAotSource",
                "BlazorUIWitness",
                "ActualChat.UI.Blazor.Module",
                "UI.Blazor/Module/BlazorUIAotSource.g.cs",
                "UI.Blazor/Module/BlazorUIModuleInitializer.g.cs",
                // Serializable kept here so the StoredState<T>/ISyncedState<T> field scan
                // can target this witness for payloads declared in UI.Blazor (e.g.
                // StoredState<Box<bool>> in DownloadAppBanner / RightPanelStoredState).
                [AotTypeKind.Component, AotTypeKind.Serializable]),
            new("ActualChat.UI.Blazor.App",
                "BlazorUIAppAotSource",
                "BlazorUIAppWitness",
                "ActualChat.UI.Blazor.App.Module",
                "UI.Blazor.App/Module/BlazorUIAppAotSource.g.cs",
                "UI.Blazor.App/Module/BlazorUIAppModuleInitializer.g.cs",
                [AotTypeKind.Component, AotTypeKind.Serializable]),
        };

        return explicitTargets;
    }

    public static int Generate(string? projectRoot)
    {
        projectRoot ??= FindProjectRoot();
        if (projectRoot == null) {
            Error.WriteLine("Error: Could not find project root (no ActualChat.sln found in any parent directory).");
            return 1;
        }

        var srcDotnet = Path.Combine(projectRoot, "src", "dotnet");
        if (!Directory.Exists(srcDotnet)) {
            Error.WriteLine($"Error: src/dotnet directory not found at: {srcDotnet}");
            return 1;
        }

        LoadAssemblies();
        var components = DiscoverTypes(typeof(ComponentBase), includeAbstract: false, interfacesOnly: false);
        // IRpcService covers IComputeService, ICommandService, plain RPC services, and any
        // other Fusion-side RPC contract — catches all interfaces whose methods reach the
        // wire, so the signature scan below sees every closed payload type.
        var apis = DiscoverTypes(typeof(IRpcService), includeAbstract: true, interfacesOnly: true);
        var serializables = DiscoverSerializableTypes();
        // Enums and other closed types that reach the wire ONLY via RPC method signatures
        // (no [MemoryPackable], no property of a witnessed DTO) — e.g. the Presence enum
        // returned by IUserPresences.Get. Without this, graph walk misses them and the
        // codegen-only client can't deserialize the response.
        var alreadySerializable = new HashSet<Type>(serializables);
        var rpcSignatureTypesByAssembly = DiscoverRpcSignatureTypes(apis, alreadySerializable);
        var rpcSignatureTypesTotal = rpcSignatureTypesByAssembly.Values.Sum(l => l.Count);
        // Primitive payload types (double / int / Guid / …) that flow through RPC method
        // signatures. PolyType handles them intrinsically (no witness shape needed), but
        // Fusion's RPC plumbing builds NerdbankMessagePackByteSerializer<T> at runtime via
        // an Expression-compiled factory — under NativeAOT the closed generic must be Kept
        // explicitly or ILC drops it. Grouped by the assembly that DECLARED the interface.
        var rpcPrimitiveTypesByAssembly = DiscoverRpcSignaturePrimitives(apis);
        var rpcPrimitiveTypesTotal = rpcPrimitiveTypesByAssembly.Values.Sum(l => l.Count);
        // Closed payload T's of IStoredState<T> / ISyncedState<T> fields and properties. The
        // KVAS-stored type may only appear as a field type, never as a property of a
        // witnessed DTO, so the graph walk would miss them. Grouped by the field owner's
        // assembly so the existing target's witness emits the shape.
        var storedStateTypesByAssembly = DiscoverStoredStateTypes(alreadySerializable);
        var storedStateTypesTotal = storedStateTypesByAssembly.Values.Sum(l => l.Count);

        WriteLine($"Discovered {components.Count} Blazor components");
        WriteLine($"Discovered {apis.Count} API interfaces");
        WriteLine($"Discovered {serializables.Count} serializable types");
        WriteLine($"Discovered {rpcSignatureTypesTotal} RPC-signature-only types (enums, etc.)");
        WriteLine($"Discovered {rpcPrimitiveTypesTotal} RPC-signature primitive types (double/Guid/…)");
        WriteLine($"Discovered {storedStateTypesTotal} IStoredState/ISyncedState payload types");

        // Discover STJ converter types needed for JS interop
        WriteLine("Discovering STJ converter types...");
        var stjConverters = StjConverterDiscovery.DiscoverAll();
        WriteLine($"Discovered {stjConverters.Count} STJ converter types");

        // Discover Fusion ComputedStateComponent state types
        var stateFactoryKeeps = DiscoverComputedStateFactoryKeeps();
        WriteLine($"Discovered {stateFactoryKeeps.Count} ComputedStateComponent state factory types");
        foreach (var aqn in stateFactoryKeeps)
            stjConverters.Add(aqn);

        foreach (var target in Targets) {
            // Collect types from all requested kinds, filtered to the target assembly
            var filtered = new List<(Type Type, AotTypeKind Kind)>();
            foreach (var kind in target.TypeKinds) {
                var source = kind switch {
                    AotTypeKind.Api => apis,
                    AotTypeKind.Component => components,
                    AotTypeKind.Serializable => serializables,
                    _ => [],
                };
                filtered.AddRange(source
                    .Where(t => t.Assembly.GetName().Name == target.AssemblyName)
                    .Select(t => (t, kind)));
            }

            // Fold RPC-signature-only types into the target whose assembly declared the
            // owning interface — that's the only assembly whose witness can name a closed
            // generic like ApiArray<LiveStreamInfo> (definition lives in ActualLab.Core,
            // but ApiContractsModuleInitializer can reference it via its using directives).
            if (target.TypeKinds.Contains(AotTypeKind.Serializable)) {
                if (rpcSignatureTypesByAssembly.TryGetValue(target.AssemblyName, out var rpcTypes))
                    filtered.AddRange(rpcTypes.Select(t => (t, AotTypeKind.Serializable)));

                // Fold IStoredState<T> / ISyncedState<T> payload T's for any field declared in
                // this target's assembly. The T itself may live elsewhere (collection/wrapper
                // types from System or Fusion), but the shape must be emitted in the witness
                // for the assembly where the field is declared — that's the assembly whose
                // codegen sees the closed T and can reference it.
                if (storedStateTypesByAssembly.TryGetValue(target.AssemblyName, out var storedStateTypes))
                    filtered.AddRange(storedStateTypes.Select(t => (t, AotTypeKind.Serializable)));
            }

            // STJ converter keeps go into the UI.Blazor AotSource
            var stjKeeps = target.AssemblyName == "ActualChat.UI.Blazor" ? stjConverters : null;

            // Framework type keeps go into the Core AotSource
            var frameworkKeeps = target.AssemblyName == "ActualChat.Core" ? FrameworkTypeKeeps : null;

            // RPC-signature primitives (double / int / Guid / …): emit per-type
            // KeepNerdbankSerializer<T>() in the AotSource that owns the interface.
            rpcPrimitiveTypesByAssembly.TryGetValue(target.AssemblyName, out var rpcPrimitives);

            var witnessPath = Path.Combine(srcDotnet, target.WitnessRelativePath.Replace('/', Path.DirectorySeparatorChar));
            // Dedupe — the same type may come in via multiple discovery sources
            // (e.g., both the RPC-signature scan and the StoredState scan pick up a closed
            // collection). Duplicate [GenerateShapeFor<T>] attributes on one witness = compile
            // error, so collapse them here.
            var serializableTypes = filtered
                .Where(x => x.Kind == AotTypeKind.Serializable)
                .Select(x => x.Type)
                .Distinct()
                .ToList();
            var hasWitness = serializableTypes.Count > 0;
            var hasAotSource = target.AotSourceRelativePath is not null;

            if (hasAotSource) {
                var aotSourcePath = Path.Combine(srcDotnet, target.AotSourceRelativePath!.Replace('/', Path.DirectorySeparatorChar));
                var aotSourceDir = Path.GetDirectoryName(aotSourcePath);
                if (!string.IsNullOrEmpty(aotSourceDir))
                    Directory.CreateDirectory(aotSourceDir);
                var aotSource = GenerateAotSourceFile(target, filtered, stjKeeps, frameworkKeeps, rpcPrimitives, hasWitness);
                File.WriteAllText(aotSourcePath, aotSource, Encoding.UTF8);
                WriteLine($"Generated: {target.AotSourceRelativePath} ({filtered.Count} types)");
            }

            if (hasWitness) {
                var witnessDir = Path.GetDirectoryName(witnessPath);
                if (!string.IsNullOrEmpty(witnessDir))
                    Directory.CreateDirectory(witnessDir);
                var content = GenerateModuleInitializerFile(target, serializableTypes, hasAotSource);
                File.WriteAllText(witnessPath, content, Encoding.UTF8);
                WriteLine($"Generated: {target.WitnessRelativePath} ({serializableTypes.Count} shape witnesses)");
            }
            else if (File.Exists(witnessPath)) {
                File.Delete(witnessPath);
                WriteLine($"Removed stale module initializer file: {target.WitnessRelativePath}");
            }
        }

        return 0;
    }

    private static string? FindProjectRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir != null) {
            if (File.Exists(Path.Combine(dir, "ActualChat.sln")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static void LoadAssemblies()
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetName().Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var name in AssemblyNames) {
            if (loaded.Contains(name))
                continue;
            try {
                var assembly = Assembly.Load(name);
                WriteLine($"Loaded: {assembly.GetName().Name}");
            }
            catch (Exception e) {
                Error.WriteLine($"Warning: Could not load {name}: {e.Message}");
            }
        }
    }

    private static List<Type> DiscoverTypes(Type baseType, bool includeAbstract, bool interfacesOnly)
    {
        var result = new List<Type>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            var name = assembly.GetName().Name ?? "";
            if (!name.StartsWith("ActualChat.", StringComparison.Ordinal))
                continue;

            try {
                foreach (var type in assembly.GetTypes()) {
                    if (interfacesOnly && !type.IsInterface)
                        continue;
                    if (!interfacesOnly && (type.IsInterface || type.IsGenericTypeDefinition))
                        continue;
                    if (!includeAbstract && type.IsAbstract)
                        continue;
                    if (!baseType.IsAssignableFrom(type))
                        continue;
                    if (type == baseType)
                        continue;
                    result.Add(type);
                }
            }
            catch (ReflectionTypeLoadException e) {
                foreach (var type in e.Types) {
                    if (type == null)
                        continue;
                    if (interfacesOnly && !type.IsInterface)
                        continue;
                    if (!interfacesOnly && (type.IsInterface || type.IsGenericTypeDefinition))
                        continue;
                    if (!includeAbstract && type.IsAbstract)
                        continue;
                    if (!baseType.IsAssignableFrom(type))
                        continue;
                    if (type == baseType)
                        continue;
                    result.Add(type);
                }
            }
        }

        result.Sort((a, b) => string.Compare(
            a.FullName, b.FullName, StringComparison.Ordinal));
        return result;
    }

    /// <summary>
    /// Discovers serializable DTO types for the post-MessagePack world: anything carrying
    /// <c>[MemoryPackable]</c>. MemoryPack stays as the back-up binary serializer and covers
    /// every DTO we used to mark with <c>[MessagePackObject]</c>; its marker is what the
    /// generator now keys off.
    /// </summary>
    private static List<Type> DiscoverSerializableTypes()
    {
        var result = new List<Type>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            var name = assembly.GetName().Name ?? "";
            if (!name.StartsWith("ActualChat.", StringComparison.Ordinal))
                continue;

            try {
                foreach (var type in assembly.GetTypes())
                    Visit(type);
            }
            catch (ReflectionTypeLoadException e) {
                foreach (var type in e.Types)
                    if (type is not null)
                        Visit(type);
            }
        }

        result.Sort((a, b) => string.Compare(
            a.FullName, b.FullName, StringComparison.Ordinal));
        return result;

        void Visit(Type type)
        {
            if (type.IsInterface || type.IsGenericTypeDefinition)
                return;
            if (!HasSerializableMarker(type))
                return;
            // Abstract bases are kept ONLY if they're union roots — i.e. they carry one or
            // more [DerivedTypeShape] markers — because the union dispatcher needs a shape
            // for the base to read the discriminator and pick the subtype. Plain abstract
            // [MemoryPackable] classes with no derived-type markers are never instantiated
            // on the wire, so emitting a shape for them just bloats the witness.
            if (type.IsAbstract && !HasDerivedTypeShape(type))
                return;
            result.Add(type);
        }
    }

    private static bool HasDerivedTypeShape(Type type)
    {
        foreach (var a in type.CustomAttributes) {
            var n = a.AttributeType.FullName;
            if (n is not null && n.StartsWith("PolyType.DerivedTypeShapeAttribute", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static bool HasSerializableMarker(Type type)
    {
        foreach (var a in type.CustomAttributes) {
            var n = a.AttributeType.FullName;
            if (n is "MemoryPack.MemoryPackableAttribute")
                return true;
        }
        return false;
    }

    /// <summary>
    /// Walks every field / property across every ActualChat type looking for
    /// <c>IStoredState&lt;T&gt;</c> and <c>ISyncedState&lt;T&gt;</c> instances (or subclasses).
    /// The closed <typeparamref name="T"/> of each hit is grouped by the declaring type's
    /// assembly so the owning witness can emit the shape. Without this, KVAS payloads that
    /// live only in a field type — never as a property of a witnessed DTO — are invisible
    /// to PolyType's graph walk and the codegen-only client can't deserialize them.
    /// </summary>
    private static Dictionary<string, List<Type>> DiscoverStoredStateTypes(HashSet<Type> alreadySerializable)
    {
        var byAssembly = new Dictionary<string, List<Type>>(StringComparer.Ordinal);
        var seenByAssembly = new Dictionary<string, HashSet<Type>>(StringComparer.Ordinal);
        const BindingFlags flags =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            var asmName = assembly.GetName().Name ?? "";
            if (!asmName.StartsWith("ActualChat.", StringComparison.Ordinal))
                continue;

            Type?[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types; }

            foreach (var owner in types) {
                if (owner is null)
                    continue;
                try {
                    foreach (var field in owner.GetFields(flags))
                        MaybeAdd(asmName, ExtractStoredStateArg(field.FieldType));
                    foreach (var prop in owner.GetProperties(flags))
                        MaybeAdd(asmName, ExtractStoredStateArg(prop.PropertyType));
                }
                catch { /* Unloadable or malformed member — skip. */ }
            }
        }

        foreach (var list in byAssembly.Values)
            list.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.Ordinal));
        return byAssembly;

        void MaybeAdd(string asmName, Type? t)
        {
            if (t is null)
                return;
            if (!IsShapeableStoredStateArg(t, alreadySerializable))
                return;
            if (!seenByAssembly.TryGetValue(asmName, out var seen)) {
                seen = new HashSet<Type>();
                seenByAssembly[asmName] = seen;
            }
            if (!seen.Add(t))
                return;
            if (!byAssembly.TryGetValue(asmName, out var list)) {
                list = new List<Type>();
                byAssembly[asmName] = list;
            }
            list.Add(t);
        }
    }

    // Inspect <paramref name="type"/> (and its interfaces) for an IStoredState<T> /
    // ISyncedState<T> implementation and return the T argument, or null if none.
    private static Type? ExtractStoredStateArg(Type type)
    {
        if (TryExtract(type, out var t))
            return t;
        foreach (var iface in type.GetInterfaces())
            if (TryExtract(iface, out t))
                return t;
        return null;

        static bool TryExtract(Type candidate, out Type? arg)
        {
            arg = null;
            if (!candidate.IsGenericType)
                return false;
            var def = candidate.GetGenericTypeDefinition();
            var defName = def.FullName;
            if (defName is "ActualChat.Kvas.IStoredState`1" or "ActualChat.Kvas.ISyncedState`1") {
                arg = candidate.GetGenericArguments()[0];
                return true;
            }
            return false;
        }
    }

    private static bool IsShapeableStoredStateArg(Type type, HashSet<Type> alreadySerializable)
    {
        if (type.FullName is null)
            return false;
        if (type.IsGenericTypeDefinition || type.IsGenericParameter || type.IsPointer || type.IsByRef)
            return false;
        // Something like Range<T> where T is the declaring class's open generic parameter —
        // the type is "closed" syntactically but contains an unbound parameter, so emitting
        // [GenerateShapeFor<Range<T>>] would produce nonsense C#.
        if (type.ContainsGenericParameters)
            return false;
        // Primitives / built-ins handled intrinsically by PolyType — no witness entry needed.
        if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal)
            || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan)
            || type == typeof(Guid))
            return false;
        // Already covered by the serializable scan.
        return !alreadySerializable.Contains(type);
    }

    /// <summary>
    /// Walks every API interface (<see cref="IComputeService"/> / <see cref="ICommandService"/>
    /// and friends) and collects every type that appears as a method parameter or return,
    /// minus wrappers (<c>Task&lt;T&gt;</c>, <c>ValueTask&lt;T&gt;</c>, <c>CancellationToken</c>,
    /// primitives, and types that already carry <c>[MemoryPackable]</c> — those are handled
    /// by the serializable scan). The result is grouped by the assembly that DECLARES the
    /// interface, not the assembly that owns the closed generic — that way a closed
    /// <c>ApiArray&lt;LiveStreamInfo&gt;</c> (whose definition lives in ActualLab.Core)
    /// lands in the witness for ActualChat.Api.Contracts, which is where ILiveAudioStreams
    /// is declared and where the closed shape can actually be referenced from C#.
    /// </summary>
    private static Dictionary<string, List<Type>> DiscoverRpcSignatureTypes(
        List<Type> apiInterfaces,
        HashSet<Type> alreadySerializable)
    {
        var byAssembly = new Dictionary<string, List<Type>>(StringComparer.Ordinal);
        var seenByAssembly = new Dictionary<string, HashSet<Type>>(StringComparer.Ordinal);

        foreach (var api in apiInterfaces) {
            var asmName = api.Assembly.GetName().Name ?? "";
            if (!seenByAssembly.TryGetValue(asmName, out var seen)) {
                seen = new HashSet<Type>();
                seenByAssembly[asmName] = seen;
            }
            if (!byAssembly.TryGetValue(asmName, out var list)) {
                list = new List<Type>();
                byAssembly[asmName] = list;
            }
            foreach (var method in api.GetMethods(BindingFlags.Public | BindingFlags.Instance)) {
                CollectType(UnwrapAsync(method.ReturnType), seen, list);
                foreach (var parameter in method.GetParameters())
                    CollectType(UnwrapAsync(parameter.ParameterType), seen, list);
            }
        }

        foreach (var list in byAssembly.Values)
            list.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.Ordinal));
        return byAssembly;

        void CollectType(Type? type, HashSet<Type> seen, List<Type> list)
        {
            if (type is null)
                return;
            if (!seen.Add(type))
                return;
            if (IsRelevantShapeType(type, alreadySerializable))
                list.Add(type);

            // Walk generic args so e.g. `Task<IReadOnlyList<Presence>>` yields Presence.
            if (type.IsGenericType) {
                foreach (var arg in type.GetGenericArguments())
                    CollectType(arg, seen, list);

                // RpcStream<T> sends individual items via $sys.I (shape: T) and batches via
                // $sys.B (shape: T[]). The first is covered by walking generic args above;
                // the second only appears at runtime as the batch payload — no signature
                // mentions T[] anywhere — so we synthesize it here for every RpcStream<T>.
                //
                // Plus, RpcStreamNerdbankConverter resolves the per-stream HostId via
                // context.GetConverter<Guid> against the WITNESS-LOCAL provider (not the
                // aggregator), so every witness holding a closed RpcStream<T> also needs a
                // Guid shape entry — synthesize it next to T[] so the discovery is symmetric.
                if (type.GetGenericTypeDefinition() == typeof(RpcStream<>)) {
                    var elementType = type.GetGenericArguments()[0];
                    if (elementType.FullName != null && !elementType.ContainsGenericParameters)
                        CollectType(elementType.MakeArrayType(), seen, list);
                    AddRpcStreamCompanionShapes(seen, list);
                }
            }
            if (type.IsArray)
                CollectType(type.GetElementType(), seen, list);
        }
    }

    // Helper types whose shapes RpcStreamNerdbankConverter resolves against the witness-local
    // provider — must be present in any witness containing a closed RpcStream<T> entry,
    // even though they wouldn't pass the normal IsRelevantShapeType filter (Guid is a
    // primitive otherwise excluded).
    private static readonly Type[] RpcStreamCompanionTypes = [typeof(Guid)];

    private static void AddRpcStreamCompanionShapes(HashSet<Type> seen, List<Type> list)
    {
        foreach (var t in RpcStreamCompanionTypes) {
            // Track in seen so later filtered visits skip it, but emit unconditionally if
            // not already present in this assembly's list (the seen set may already contain
            // it from a normal walk where IsRelevantShapeType filtered it out).
            seen.Add(t);
            if (!list.Contains(t))
                list.Add(t);
        }
    }

    /// <summary>
    /// Walks every RPC interface method and collects the primitive / built-in payload types
    /// (<c>double</c>, <c>int</c>, <c>Guid</c>, …) that appear as parameters or returns —
    /// the ones <see cref="IsRelevantShapeType"/> filters out because PolyType handles them
    /// intrinsically. We still need to Keep the closed
    /// <c>NerdbankMessagePackByteSerializer&lt;T&gt;</c> for each, otherwise NativeAOT throws
    /// "missing native code or metadata" the first time Fusion's RPC plumbing tries to build
    /// the typed serializer at runtime.
    /// </summary>
    private static Dictionary<string, List<Type>> DiscoverRpcSignaturePrimitives(List<Type> apiInterfaces)
    {
        var byAssembly = new Dictionary<string, List<Type>>(StringComparer.Ordinal);
        var seenByAssembly = new Dictionary<string, HashSet<Type>>(StringComparer.Ordinal);

        foreach (var api in apiInterfaces) {
            var asmName = api.Assembly.GetName().Name ?? "";
            if (!seenByAssembly.TryGetValue(asmName, out var seen)) {
                seen = new HashSet<Type>();
                seenByAssembly[asmName] = seen;
            }
            if (!byAssembly.TryGetValue(asmName, out var list)) {
                list = new List<Type>();
                byAssembly[asmName] = list;
            }
            foreach (var method in api.GetMethods(BindingFlags.Public | BindingFlags.Instance)) {
                Walk(UnwrapAsync(method.ReturnType), seen, list);
                foreach (var parameter in method.GetParameters())
                    Walk(UnwrapAsync(parameter.ParameterType), seen, list);
            }
        }

        foreach (var list in byAssembly.Values)
            list.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.Ordinal));
        return byAssembly;

        static void Walk(Type? type, HashSet<Type> seen, List<Type> list)
        {
            if (type is null || !seen.Add(type))
                return;
            if (IsRpcSignaturePrimitive(type))
                list.Add(type);
            // Walk generic args so e.g. Task<List<double>> still surfaces double.
            if (type.IsGenericType)
                foreach (var arg in type.GetGenericArguments())
                    Walk(arg, seen, list);
            if (type.IsArray)
                Walk(type.GetElementType(), seen, list);
        }
    }

    // Mirrors the IsRelevantShapeType primitive filter — a type is an "RPC primitive" if
    // PolyType handles it intrinsically. CancellationToken is excluded here too: it's an
    // RPC ambient, never serialized.
    private static bool IsRpcSignaturePrimitive(Type type)
    {
        if (type == typeof(void) || type == typeof(Task) || type == typeof(ValueTask))
            return false;
        if (type == typeof(CancellationToken) || type == typeof(string))
            return false; // string already has a built-in Nerdbank converter shipped with the framework
        return type.IsPrimitive
            || type == typeof(decimal)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(TimeSpan)
            || type == typeof(Guid);
    }

    private static Type UnwrapAsync(Type type)
    {
        if (!type.IsGenericType)
            return type;
        var def = type.GetGenericTypeDefinition();
        if (def == typeof(Task<>) || def == typeof(ValueTask<>) || def == typeof(IAsyncEnumerable<>))
            return type.GetGenericArguments()[0];
        return type;
    }

    private static bool IsRelevantShapeType(Type type, HashSet<Type> alreadySerializable)
    {
        // Skip the universal "no-result" wrappers.
        if (type == typeof(void) || type == typeof(Task) || type == typeof(ValueTask))
            return false;
        // Skip things that aren't sensible shape targets.
        if (type.IsGenericTypeDefinition || type.IsGenericParameter || type.IsPointer || type.IsByRef)
            return false;
        if (type.ContainsGenericParameters)
            return false;
        if (type.FullName == null)
            return false;
        // Primitives, strings, decimals, DateTime, CancellationToken — PolyType handles
        // these intrinsically, no witness entry needed.
        if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal)
            || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan)
            || type == typeof(Guid) || type == typeof(CancellationToken))
            return false;
        // [MemoryPackable] types already land in the serializable scan.
        if (alreadySerializable.Contains(type))
            return false;
        // Closed generic instances live in their open definition's assembly (e.g.
        // ApiArray<LiveStreamInfo> is "owned" by ActualLab.Core), but the wire payload
        // carries an ActualChat type as the type argument. Accept the closed type as long
        // as either the definition or any generic arg comes from ActualChat or ActualLab —
        // pure System.* generics get filtered out (their elements are walked separately).
        return IsAppOwned(type);

        static bool IsAppOwned(Type t)
        {
            if (StartsWithKnownPrefix(t.Assembly.GetName().Name))
                return true;
            if (t.IsGenericType)
                foreach (var arg in t.GetGenericArguments())
                    if (IsAppOwned(arg))
                        return true;
            if (t.IsArray)
                return IsAppOwned(t.GetElementType()!);
            return false;
        }

        static bool StartsWithKnownPrefix(string? asmName)
            => asmName is not null
                && (asmName.StartsWith("ActualChat.", StringComparison.Ordinal)
                    || asmName.StartsWith("ActualLab.", StringComparison.Ordinal));
    }

    private static string GenerateAotSourceFile(
        AotSourceTarget target,
        List<(Type Type, AotTypeKind Kind)> types,
        SortedSet<string>? stjConverterAqns,
        IReadOnlyList<string>? frameworkTypeKeeps,
        IReadOnlyList<Type>? rpcPrimitiveTypes,
        bool hasWitness)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("// This file is generated by App.AotHelper -g. Do not edit manually.");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine();
        sb.AppendLine("#pragma warning disable CS0618 // Obsolete types are still needed for AOT retention");
        sb.AppendLine();
        sb.AppendLine("using ActualChat.Aot;");
        sb.AppendLine();
        sb.AppendLine($"namespace {target.Namespace};");
        sb.AppendLine();
        sb.AppendLine($"internal partial class {target.ClassName} : IAotSource");
        sb.AppendLine("{");

        // KeepTypes method
        sb.AppendLine("    public void KeepTypes()");
        sb.AppendLine("    {");
        var hasContent = types.Count > 0
            || stjConverterAqns is { Count: > 0 }
            || frameworkTypeKeeps is { Count: > 0 }
            || rpcPrimitiveTypes is { Count: > 0 };
        if (hasContent) {
            sb.AppendLine("        if (CodeKeeper.AlwaysTrue)");
            sb.AppendLine("            return;");
            sb.AppendLine();
            foreach (var (type, kind) in types) {
                var typeName = FormatTypeName(type);
                if (typeName == null)
                    continue;
                if (kind == AotTypeKind.Serializable) {
                    // The per-project witness implements IShapeable<T> for every serializable
                    // in this assembly, so AotTypes.KeepSerializable keeps T + its Nerdbank
                    // round-trip path through the source-generated shape.
                    sb.AppendLine($"        AotTypes.KeepSerializable<{typeName}, {target.WitnessClassName}>();");
                    // IStringLike<T> identifiers serialize through StringLikeNerdbankConverter<T>,
                    // which Serializers.RegisterStringLikeTypes instantiates at runtime via
                    // MakeGenericType + Activator. Without an explicit Keep, the closed converter
                    // isn't preserved by ILC and NativeAOT throws "missing native code or metadata".
                    if (ImplementsIStringLike(type))
                        sb.AppendLine($"        AotTypes.KeepStringLikeConverter<{typeName}>();");
                }
                else {
                    sb.AppendLine($"        CodeKeeper.Keep<{typeName}>();");
                }
            }
            if (frameworkTypeKeeps is { Count: > 0 }) {
                sb.AppendLine();
                sb.AppendLine("        // Framework types referenced by BCL / runtime code paths");
                foreach (var typeName in frameworkTypeKeeps)
                    sb.AppendLine($"        CodeKeeper.Keep<{typeName}>();");
            }
            if (rpcPrimitiveTypes is { Count: > 0 }) {
                sb.AppendLine();
                sb.AppendLine("        // Closed NerdbankMessagePackByteSerializer<T> for primitives that flow through");
                sb.AppendLine("        // RPC method signatures — Fusion builds these at runtime via Expression-compiled");
                sb.AppendLine("        // factories which ILC can't pre-resolve.");
                foreach (var primitive in rpcPrimitiveTypes) {
                    var typeName = FormatTypeName(primitive);
                    if (typeName != null)
                        sb.AppendLine($"        AotTypes.KeepNerdbankSerializer<{typeName}>();");
                }
            }
            if (stjConverterAqns is { Count: > 0 }) {
                sb.AppendLine();
                sb.AppendLine("        // STJ internal converter types for JS interop (auto-discovered)");
                foreach (var aqn in stjConverterAqns)
                    sb.AppendLine($"        CodeKeeper.Keep(\"{EscapeString(NormalizeAqn(aqn))}\");");
            }
        }
        sb.AppendLine("    }");
        sb.AppendLine();

        // ListTypes method
        sb.AppendLine($"    public (Type, AotTypeKind)[] ListTypes()");
        sb.AppendLine("        => [");
        foreach (var (type, kind) in types) {
            var typeName = FormatTypeName(type);
            if (typeName != null)
                sb.AppendLine($"            (typeof({typeName}), AotTypeKind.{kind}),");
        }
        sb.AppendLine("        ];");

        sb.AppendLine("}");
        _ = hasWitness; // reserved for future use; witness existence is already reflected in keep emission above
        return sb.ToString();
    }

    private static string GenerateModuleInitializerFile(AotSourceTarget target, List<Type> serializableTypes, bool hasAotSource)
    {
        var initializerClassName = DeriveModuleInitializerClassName(target.WitnessClassName);
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("// This file is generated by App.AotHelper -g. Do not edit manually.");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine("//");
        sb.AppendLine($"// PolyType witness for every [Serializable] AOT root in {target.AssemblyName}. Each");
        sb.AppendLine("// [GenerateShapeFor<T>] attribute makes this class implement IShapeable<T>, which the");
        sb.AppendLine("// Nerdbank.MessagePack serializer can use to round-trip T under NativeAOT without");
        sb.AppendLine("// touching the reflection shape provider.");
        sb.AppendLine("//");
        sb.AppendLine($"// The generated half of {initializerClassName} self-registers the shape provider (and the");
        sb.AppendLine("// AOT source, when present) via [ModuleInitializer], so the hand-written half only");
        sb.AppendLine("// needs to declare its own dependency chain through its static constructor + Load().");
        sb.AppendLine();
        sb.AppendLine("#pragma warning disable CS0618 // Obsolete types are still needed for AOT retention");
        sb.AppendLine("#pragma warning disable CA2255  // ModuleInitializer is intended for AOT setup");
        sb.AppendLine();
        sb.AppendLine("using PolyType;");
        sb.AppendLine();
        sb.AppendLine($"namespace {target.Namespace};");
        sb.AppendLine();
        foreach (var type in serializableTypes) {
            var typeName = FormatTypeName(type);
            if (typeName == null)
                continue;
            sb.AppendLine($"[GenerateShapeFor<{typeName}>]");
        }
        sb.AppendLine($"internal partial class {target.WitnessClassName};");
        sb.AppendLine();
        sb.AppendLine($"public static partial class {initializerClassName}");
        sb.AppendLine("{");
        sb.AppendLine("    [global::System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine("    internal static void RegisterGenerated()");
        sb.AppendLine("    {");
        if (hasAotSource)
            sb.AppendLine($"        global::ActualChat.Aot.AotTypes.AddSource(new {target.ClassName}());");
        sb.AppendLine($"        global::ActualChat.Serialization.Serializers.RegisterShapeProvider({target.WitnessClassName}.GeneratedTypeShapeProvider);");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    // Witness class name is "<prefix>Witness" (e.g. "ApiContractsWitness"); the matching
    // initializer class is "<prefix>ModuleInitializer" (e.g. "ApiContractsModuleInitializer"),
    // which the hand-written static partial declares with a static .ctor (for the dependency
    // chain) + an empty public Load() (to trigger type init cross-assembly).
    private static string DeriveModuleInitializerClassName(string witnessClassName)
    {
        const string suffix = "Witness";
        return witnessClassName.EndsWith(suffix, StringComparison.Ordinal)
            ? witnessClassName[..^suffix.Length] + "ModuleInitializer"
            : witnessClassName + "ModuleInitializer";
    }

    // True iff `type` directly implements IStringLike<TSelf> for itself — i.e. it's an
    // identifier struct/record routed through StringLikeNerdbankConverter<T> at runtime.
    private static bool ImplementsIStringLike(Type type)
    {
        foreach (var i in type.GetInterfaces()) {
            if (!i.IsGenericType)
                continue;
            var def = i.GetGenericTypeDefinition();
            if (def.FullName == "ActualChat.IStringLike`1" && i.GetGenericArguments()[0] == type)
                return true;
        }
        return false;
    }

    private static string? FormatTypeName(Type type)
    {
        if (type.FullName == null)
            return null;
        if (type.IsGenericTypeDefinition || type.ContainsGenericParameters)
            return null; // can't be expressed inline; caller is responsible for filtering.

        if (type.IsArray) {
            var element = FormatTypeName(type.GetElementType()!);
            return element is null ? null : element + "[]";
        }
        if (type.IsGenericType) {
            // Closed generic: render as `Outer.Generic<arg1, arg2>` recursively.
            var def = type.GetGenericTypeDefinition();
            var args = type.GetGenericArguments();
            var defName = StripBacktick(def.Name);
            var owner = FormatDeclaringChain(def.DeclaringType, def.Namespace);
            var argList = string.Join(", ", args.Select(FormatTypeName));
            return owner is null ? $"global::{defName}<{argList}>" : $"{owner}.{defName}<{argList}>";
        }

        // Walk the declaring-type chain so nested types render as
        // `global::Ns.Outer.Inner` rather than the stripped `global::Ns.Inner`
        // (which is what `type.Namespace + type.Name` alone produces).
        var segments = new List<string>();
        for (var t = type; t is not null; t = t.DeclaringType)
            segments.Add(t.Name);
        segments.Reverse();
        var chain = string.Join(".", segments);
        return type.Namespace is { } ns ? $"global::{ns}.{chain}" : $"global::{chain}";
    }

    private static string StripBacktick(string name)
    {
        var idx = name.IndexOf('`');
        return idx < 0 ? name : name[..idx];
    }

    private static string? FormatDeclaringChain(Type? declaringType, string? @namespace)
    {
        if (declaringType is null)
            return @namespace is null ? null : $"global::{@namespace}";
        var owner = FormatDeclaringChain(declaringType.DeclaringType, declaringType.Namespace);
        return owner is null
            ? $"global::{StripBacktick(declaringType.Name)}"
            : $"{owner}.{StripBacktick(declaringType.Name)}";
    }

    /// <summary>
    /// Discovers state types T from all ComputedStateComponent&lt;T&gt; descendants
    /// and returns CodeKeeper.Keep AQNs for CreateDefaultStateOptionsFactory&lt;T&gt;.
    /// </summary>
    private static List<string> DiscoverComputedStateFactoryKeeps()
    {
        var computedStateComponentType = typeof(ActualLab.Fusion.Blazor.ComputedStateComponent<>);
        var factoryTypeName = "ActualLab.Fusion.Blazor.ComputedStateComponent+CreateDefaultStateOptionsFactory`1";
        var factoryAssembly = "ActualLab.Fusion.Blazor";
        var stateTypes = new HashSet<Type>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            var name = assembly.GetName().Name ?? "";
            if (!name.StartsWith("ActualChat.", StringComparison.Ordinal))
                continue;

            try {
                foreach (var type in assembly.GetTypes()) {
                    if (type.IsAbstract || type.IsGenericTypeDefinition)
                        continue;

                    // Walk the inheritance chain to find ComputedStateComponent<T>
                    var current = type.BaseType;
                    while (current != null) {
                        if (current.IsGenericType
                            && current.GetGenericTypeDefinition() == computedStateComponentType) {
                            var stateType = current.GetGenericArguments()[0];
                            if (!stateType.IsGenericParameter)
                                stateTypes.Add(stateType);
                            break;
                        }
                        current = current.BaseType;
                    }
                }
            }
            catch (ReflectionTypeLoadException) { }
        }

        var result = new List<string>();
        foreach (var stateType in stateTypes.OrderBy(t => t.FullName)) {
            var stateAqn = stateType.AssemblyQualifiedName;
            if (stateAqn == null)
                continue;
            var factoryAqn = $"{factoryTypeName}[[{stateAqn}]], {factoryAssembly}";
            result.Add(factoryAqn);
        }
        return result;
    }

    private static string EscapeString(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private record AotSourceTarget(
        string AssemblyName,
        string ClassName,
        string WitnessClassName,
        string Namespace,
        string? AotSourceRelativePath,
        string WitnessRelativePath,
        AotTypeKind[] TypeKinds);
}
