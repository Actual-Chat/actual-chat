namespace ActualChat.Testing.ApiContracts;

public enum ApiTypeRefKind
{
    // Polymorphic or untyped: written with its runtime type, so there is no static shape to compare
    Any = 0,
    Primitive,
    Enum,
    // A type with its own formatter: compared by name only
    Opaque,
    OpaqueStruct,
    // A [MessagePackObject] or [Union] type, described in ApiContract.Types
    Defined,
    Nullable,
    List,
    Map,
}

/// <summary>
/// A type as <see cref="ApiContract"/> writes it: <c>any</c>, <c>prim:Int32</c>, <c>enum:Name:Int32</c>,
/// <c>opaque:Name</c>, <c>vopaque:Name</c>, <c>type:Name</c>, <c>nullable&lt;T&gt;</c>,
/// <c>list&lt;T&gt;</c> or <c>map&lt;K, V&gt;</c>.
/// </summary>
public sealed record ApiTypeRef(ApiTypeRefKind Kind, string Name = "", IReadOnlyList<ApiTypeRef>? Args = null)
{
    public static readonly ApiTypeRef Any = new(ApiTypeRefKind.Any);

    public IReadOnlyList<ApiTypeRef> Args { get; init; } = Args ?? [];

    public bool AcceptsNil
        // False for a value type that isn't Nullable<T>; a Defined struct is decided by its ApiTypeShape
        => Kind switch {
            ApiTypeRefKind.Primitive => Name is "String" or "Bytes",
            ApiTypeRefKind.Enum or ApiTypeRefKind.OpaqueStruct => false,
            _ => true,
        };

    public string EnumUnderlyingName
        // The integral type an enum is written as - what a reader actually compares
        => Kind == ApiTypeRefKind.Enum ? Name[(Name.LastIndexOf(':') + 1)..] : "";

    public static ApiTypeRef Primitive(string name) => new(ApiTypeRefKind.Primitive, name);
    public static ApiTypeRef Defined(string name) => new(ApiTypeRefKind.Defined, name);
    public static ApiTypeRef Nullable(ApiTypeRef type) => new(ApiTypeRefKind.Nullable, "", [type]);
    public static ApiTypeRef List(ApiTypeRef item) => new(ApiTypeRefKind.List, "", [item]);
    public static ApiTypeRef Map(ApiTypeRef key, ApiTypeRef value) => new(ApiTypeRefKind.Map, "", [key, value]);

    public static ApiTypeRef Enum(string name, string underlyingName)
        => new(ApiTypeRefKind.Enum, $"{name}:{underlyingName}");

    public static ApiTypeRef Opaque(string name, bool isStruct, IReadOnlyList<ApiTypeRef>? args = null)
        // A generic one, e.g. Change<T>, writes its type arguments with their own formatters
        => new(isStruct ? ApiTypeRefKind.OpaqueStruct : ApiTypeRefKind.Opaque, name, args);

    public static ApiTypeRef Parse(string s)
    {
        s = s.Trim();
        if (s == "any")
            return Any;

        var lt = s.IndexOf('<');
        if (lt > 0 && s.EndsWith('>')) {
            var args = ParseList(s[(lt + 1)..^1]);
            return s[..lt] switch {
                "nullable" => Nullable(args[0]),
                "list" => List(args[0]),
                "map" => Map(args[0], args[1]),
                var head when head.StartsWith("opaque:") => Opaque(head["opaque:".Length..], false, args),
                var head when head.StartsWith("vopaque:") => Opaque(head["vopaque:".Length..], true, args),
                var kind => throw new FormatException($"Unknown type ref kind: '{kind}'."),
            };
        }

        var colon = s.IndexOf(':');
        var name = s[(colon + 1)..];
        return s[..colon] switch {
            "prim" => Primitive(name),
            "enum" => new ApiTypeRef(ApiTypeRefKind.Enum, name),
            "opaque" => Opaque(name, false),
            "vopaque" => Opaque(name, true),
            "type" => Defined(name),
            var kind => throw new FormatException($"Unknown type ref kind: '{kind}'."),
        };
    }

    public static IReadOnlyList<ApiTypeRef> ParseList(string s)
    {
        var result = new List<ApiTypeRef>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < s.Length; i++) {
            switch (s[i]) {
            case '<' or '[':
                depth++;
                break;
            case '>' or ']':
                depth--;
                break;
            case ',' when depth == 0:
                result.Add(Parse(s[start..i]));
                start = i + 1;
                break;
            }
        }
        if (!s[start..].IsNullOrWhiteSpace())
            result.Add(Parse(s[start..]));
        return result;
    }

    public override string ToString()
        => Kind switch {
            ApiTypeRefKind.Any => "any",
            ApiTypeRefKind.Primitive => $"prim:{Name}",
            ApiTypeRefKind.Enum => $"enum:{Name}",
            ApiTypeRefKind.Opaque => $"opaque:{Name}{FormatArgs()}",
            ApiTypeRefKind.OpaqueStruct => $"vopaque:{Name}{FormatArgs()}",
            ApiTypeRefKind.Defined => $"type:{Name}",
            _ => $"{Kind.ToString().ToLower()}<{string.Join(", ", Args)}>",
        };

    public bool Equals(ApiTypeRef? other)
        // Records compare collections by reference, while two refs parsed from the same text must be equal
        => other is not null && ToString() == other.ToString();

    public override int GetHashCode()
        => ToString().GetHashCode();

    // Private methods

    private string FormatArgs()
        => Args.Count == 0 ? "" : $"<{string.Join(", ", Args)}>";
}
