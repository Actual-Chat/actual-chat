using System.Text;

namespace ActualChat.Testing.ApiContracts;

/// <summary>
/// What a client of one release sends and reads over RPC: every client-facing method by wire id,
/// and the MessagePack layout of every type reachable from their arguments and results.
/// </summary>
public sealed record ApiContract(
    Version Version,
    IReadOnlyList<ApiMethodShape> Methods,
    IReadOnlyDictionary<string, ApiTypeShape> Types)
{
    private const string Indent = "  ";

    public static ApiContract Parse(string text)
    {
        Version? version = null;
        var methods = new List<ApiMethodShape>();
        var types = new Dictionary<string, ApiTypeShape>();
        ApiTypeShape? type = null;
        foreach (var line in text.Split('\n').Select(x => x.TrimEnd('\r'))) {
            if (line.IsNullOrWhiteSpace())
                continue;

            if (line.StartsWith("version ")) {
                version = Version.Parse(line["version ".Length..]);
                continue;
            }

            if (line.StartsWith(Indent)) {
                var member = ApiMemberShape.Parse(line[Indent.Length..]);
                type = type! with { Members = [..type.Members, member] };
                types[type.Name] = type;
                continue;
            }

            var (head, tail) = SplitFirst(line);
            if (head == "method") {
                methods.Add(ApiMethodShape.Parse(tail));
                continue;
            }

            var (name, flags) = SplitFirst(tail);
            type = new ApiTypeShape(name, Enum.Parse<ApiTypeKind>(head, true), [], flags == "tolerant");
            types[name] = type;
        }
        if (version is null)
            throw new FormatException("The API contract has no version line.");

        return new ApiContract(version, methods, types);
    }

    public string Format()
    {
        var sb = new StringBuilder();
        sb.Append("version ").Append(Version.ToString(2)).Append('\n');
        foreach (var method in Methods.OrderBy(x => x.FullName))
            sb.Append("method ").Append(method.Format()).Append('\n');
        foreach (var type in Types.Values.OrderBy(x => x.Name)) {
            sb.Append(type.Kind.ToString().ToLower()).Append(' ').Append(type.Name);
            if (type.IsTolerant)
                sb.Append(" tolerant");
            sb.Append('\n');
            foreach (var member in type.Members)
                sb.Append(Indent).Append(member.Format()).Append('\n');
        }
        return sb.ToString();
    }

    // Private methods

    private static (string Head, string Tail) SplitFirst(string s)
    {
        var i = s.IndexOf(' ');
        return i < 0 ? (s, "") : (s[..i], s[(i + 1)..]);
    }
}

/// <summary>
/// One RPC method as its wire id names it. <see cref="HashCode"/> is the id itself, computed by the
/// Fusion build of the release the contract was taken from.
/// </summary>
public sealed record ApiMethodShape(
    string FullName,
    int HashCode,
    IReadOnlyList<ApiTypeRef> Arguments,
    ApiTypeRef Result)
{
    public static ApiMethodShape Parse(string s)
    {
        // "<hash> <full name> (<arg>, <arg>) -> <result>"
        var hashEnd = s.IndexOf(' ');
        var nameEnd = s.IndexOf(" (");
        var argsEnd = s.LastIndexOf(") -> ");
        var hashCode = unchecked((int)uint.Parse(s[..hashEnd], NumberStyles.HexNumber, null));
        var fullName = s[(hashEnd + 1)..nameEnd];
        var arguments = ApiTypeRef.ParseList(s[(nameEnd + 2)..argsEnd]);
        var result = ApiTypeRef.Parse(s[(argsEnd + 5)..]);
        return new ApiMethodShape(fullName, hashCode, arguments, result);
    }

    public string Format()
        => $"{unchecked((uint)HashCode):x8} {FullName} ({string.Join(", ", Arguments)}) -> {Result}";
}

public enum ApiTypeKind
{
    // Integer [Key]s: an array, where a gap in the keys is written as nil
    Object = 0,
    Struct,
    // String [Key]s: a map, where a missing member is just absent
    MapObject,
    MapStruct,
    Union,
}

public sealed record ApiTypeShape(
    string Name,
    ApiTypeKind Kind,
    IReadOnlyList<ApiMemberShape> Members,
    bool IsTolerant = false)
{
    public bool IsStruct => Kind is ApiTypeKind.Struct or ApiTypeKind.MapStruct;
    public bool IsMap => Kind is ApiTypeKind.MapObject or ApiTypeKind.MapStruct;
    public int MaxIntKey => Members.Select(x => int.TryParse(x.Key, out var k) ? k : -1).DefaultIfEmpty(-1).Max();
}

// For a union, Key is the union tag, Name is empty and Type is the member type

public sealed record ApiMemberShape(string Key, string Name, ApiTypeRef Type)
{
    public static ApiMemberShape Parse(string s)
    {
        // "<key> <name>: <type>" or, for a union, "<tag> <type>"
        var keyEnd = s.IndexOf(' ');
        var key = s[..keyEnd];
        var rest = s[(keyEnd + 1)..];
        var colon = rest.IndexOf(": ");
        return colon < 0
            ? new ApiMemberShape(key, "", ApiTypeRef.Parse(rest))
            : new ApiMemberShape(key, rest[..colon], ApiTypeRef.Parse(rest[(colon + 2)..]));
    }

    public string Format()
        => Name.IsNullOrEmpty() ? $"{Key} {Type}" : $"{Key} {Name}: {Type}";
}
