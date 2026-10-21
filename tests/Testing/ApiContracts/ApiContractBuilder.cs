using ActualChat.Serialization;
using ActualChat.Serialization.Internal;
using ActualLab.Rpc;

namespace ActualChat.Testing.ApiContracts;

/// <summary>
/// Takes the <see cref="ApiContract"/> a server presents to clients of its own version:
/// every non-backend RPC service it serves, and the MessagePack layout of the types they use.
/// </summary>
public sealed class ApiContractBuilder
{
    private readonly Dictionary<string, ApiTypeShape> _types = new();
    private readonly HashSet<Type> _visited = new();

    public static ApiContract Build(RpcServiceRegistry registry, Version version, bool mustIncludeLegacy = false)
        // A contract to save leaves out the shims kept for older clients: this version's clients
        // don't call them, and the older clients' own contracts already list them
        => new ApiContractBuilder().BuildContract(registry, version, mustIncludeLegacy);

    // Private methods

    private ApiContract BuildContract(RpcServiceRegistry registry, Version version, bool mustIncludeLegacy)
    {
        var methods = registry
            .Where(s => s is { IsSystem: false, IsBackend: false, HasServer: true })
            .SelectMany(s => s.Methods)
            .Where(m => m.IsValid && (mustIncludeLegacy || !IsLegacy(m)))
            .Select(m => new ApiMethodShape(
                m.FullName,
                m.Ref.HashCode,
                m.ParameterTypes.Where(t => t != typeof(CancellationToken)).Select(RefOf).ToList(),
                RefOf(m.UnwrappedReturnType)))
            .ToList();
        return new ApiContract(new Version(version.Major, version.Minor), methods, _types);
    }

    private ApiTypeRef RefOf(Type type)
    {
        if (type == typeof(object))
            return ApiTypeRef.Any;
        if (Nullable.GetUnderlyingType(type) is { } underlyingType)
            return ApiTypeRef.Nullable(RefOf(underlyingType));
        if (type.IsEnum)
            return ApiTypeRef.Enum(NameOf(type), Enum.GetUnderlyingType(type).Name);
        if (IsBytes(type))
            return ApiTypeRef.Primitive("Bytes");
        if (IsPrimitive(type))
            return ApiTypeRef.Primitive(type.Name);
        if (type.IsArray)
            return ApiTypeRef.List(RefOf(type.GetElementType()!));

        if (type.GetCustomAttributes<UnionAttribute>(false).Any()) {
            AddUnion(type);
            return ApiTypeRef.Defined(NameOf(type));
        }

        if (type.GetCustomAttribute<MessagePackObjectAttribute>(false) is { } objectAttribute) {
            AddObject(type, objectAttribute);
            return ApiTypeRef.Defined(NameOf(type));
        }

        if (CollectionRefOf(type) is { } collectionRef)
            return collectionRef;
        if (type.IsAbstract && type.GetCustomAttribute<MessagePackFormatterAttribute>(true) is null)
            return ApiTypeRef.Any;

        if (!type.IsGenericType)
            return ApiTypeRef.Opaque(NameOf(type), type.IsValueType);

        var args = type.GetGenericArguments().Select(RefOf).ToList();
        return ApiTypeRef.Opaque(DefinitionNameOf(type), type.IsValueType, args);
    }

    private ApiTypeRef? CollectionRefOf(Type type)
    {
        var interfaces = type.IsInterface ? [type, ..type.GetInterfaces()] : type.GetInterfaces();
        var dictionaryType = interfaces.FirstOrDefault(t => t.IsGenericType
            && t.GetGenericTypeDefinition() is var d
            && (d == typeof(IDictionary<,>) || d == typeof(IReadOnlyDictionary<,>)));
        if (dictionaryType is not null) {
            var args = dictionaryType.GetGenericArguments();
            return ApiTypeRef.Map(RefOf(args[0]), RefOf(args[1]));
        }

        var enumerableType = interfaces.FirstOrDefault(t => t.IsGenericType
            && t.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        return enumerableType is null ? null : ApiTypeRef.List(RefOf(enumerableType.GetGenericArguments()[0]));
    }

    private void AddUnion(Type type)
    {
        if (!_visited.Add(type))
            return;

        var members = type.GetCustomAttributes<UnionAttribute>(false)
            .OrderBy(a => a.Key)
            .Select(a => new ApiMemberShape(a.Key.ToString(), "", RefOf(a.SubType)))
            .ToList();
        var isTolerant = AppMessagePackResolverSettings.Formatters.TryGetValue(type, out var formatterType)
            && formatterType.IsGenericType
            && formatterType.GetGenericTypeDefinition() == typeof(ForwardCompatibleUnionFormatter<>);
        var name = NameOf(type);
        _types[name] = new ApiTypeShape(name, ApiTypeKind.Union, members, isTolerant);
    }

    private void AddObject(Type type, MessagePackObjectAttribute attribute)
    {
        if (!_visited.Add(type))
            return;

        var keyedMembers = MembersOf(type)
            .Select(m => (Member: m, Key: m.GetCustomAttribute<KeyAttribute>(true)))
            .Where(x => x.Key is not null || (attribute.KeyAsPropertyName && IsSerializableByName(x.Member)))
            .Select(x => (x.Member, Key: x.Key?.IntKey?.ToString() ?? x.Key?.StringKey ?? x.Member.Name))
            .ToList();
        var isMap = keyedMembers.Any(x => !int.TryParse(x.Key, out _));
        var members = keyedMembers
            .Select(x => new ApiMemberShape(x.Key, x.Member.Name, RefOf(MemberTypeOf(x.Member))))
            .OrderBy(x => int.TryParse(x.Key, out var k) ? k : int.MaxValue)
            .ThenBy(x => x.Key)
            .ToList();
        var kind = (isMap, type.IsValueType) switch {
            (false, false) => ApiTypeKind.Object,
            (false, true) => ApiTypeKind.Struct,
            (true, false) => ApiTypeKind.MapObject,
            (true, true) => ApiTypeKind.MapStruct,
        };
        var name = NameOf(type);
        _types[name] = new ApiTypeShape(name, kind, members);
    }

    private static IEnumerable<MemberInfo> MembersOf(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.DeclaredOnly;
        var seen = new HashSet<string>();
        for (var t = type; t is not null && t != typeof(object); t = t.BaseType) {
            var members = t.GetProperties(flags).Cast<MemberInfo>().Concat(t.GetFields(flags));
            foreach (var member in members) {
                if (member is FieldInfo field && field.IsDefined(typeof(CompilerGeneratedAttribute)))
                    continue; // Auto-property backing fields

                if (seen.Add(member.Name))
                    yield return member;
            }
        }
    }

    private static bool IsSerializableByName(MemberInfo member)
        => member is PropertyInfo { GetMethod.IsPublic: true } or FieldInfo { IsPublic: true }
            && member.GetCustomAttribute<IgnoreMemberAttribute>(true) is null;

    private static Type MemberTypeOf(MemberInfo member)
        => member switch {
            PropertyInfo p => p.PropertyType,
            FieldInfo f => f.FieldType,
            _ => throw new ArgumentOutOfRangeException(nameof(member)),
        };

    private static string NameOf(Type type)
        => type.IsGenericType
            ? $"{DefinitionNameOf(type)}[{string.Join(",", type.GetGenericArguments().Select(NameOf))}]"
            : type.FullName!;

    private static string DefinitionNameOf(Type type)
    {
        var name = type.IsGenericType ? type.GetGenericTypeDefinition().FullName! : type.FullName!;
        var tick = name.IndexOf('`');
        return tick >= 0 ? name[..tick] : name;
    }

    private static bool IsLegacy(RpcMethodDef method)
        // By convention a shim for older clients is [Obsolete] or has "Legacy" in its name
        => method.MethodInfo.IsDefined(typeof(ObsoleteAttribute)) || method.MethodInfo.Name.Contains("Legacy");

    private static bool IsBytes(Type type)
        => type == typeof(byte[]) || type == typeof(ReadOnlyMemory<byte>) || type == typeof(Memory<byte>);

    private static bool IsPrimitive(Type type)
        => type.IsPrimitive
            || type == typeof(string) || type == typeof(decimal) || type == typeof(Guid)
            || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan);
}
