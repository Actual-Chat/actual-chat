using System.Text.RegularExpressions;

namespace ActualChat.Users;

/// <summary>
/// How a new user reached the app - the <c>SourceId</c> of their <see cref="UsageEventKind.SignUp"/> event.
/// The client carries it to sign-up in the <see cref="Constants.SessionTemporals.ArrivalKey"/> session temporal.
/// </summary>
public readonly partial record struct ArrivalInfo(ArrivalKind Kind, string Id = "")
{
    public const int MaxIdLength = 64;

    public static ArrivalInfo? New(ArrivalKind kind, string? id = null)
    {
        if (kind is ArrivalKind.Web or ArrivalKind.Store)
            return id is null ? new ArrivalInfo(kind) : null;
        if (!Enum.IsDefined(kind) || !IsValidId(id))
            return null;

        return new ArrivalInfo(kind, id!);
    }

    public static ArrivalInfo Fallback(AppKind appKind)
        => new(appKind.IsMaui() ? ArrivalKind.Store : ArrivalKind.Web);

    // Accepts a local URL ("/path?a=b"), a bare query ("a=b") or a store install referrer
    public static ArrivalInfo? FromQuery(string? urlOrQuery)
    {
        if (urlOrQuery.IsNullOrEmpty())
            return null;

        var queryStart = urlOrQuery.IndexOf('?');
        var query = queryStart >= 0 ? urlOrQuery[(queryStart + 1)..] : urlOrQuery;
        var fragmentStart = query.IndexOf('#');
        if (fragmentStart >= 0)
            query = query[..fragmentStart];
        if (!query.Contains('='))
            return null;

        var items = UriExt.GetQueryCollection(query);
        var campaign = items["utm_campaign"] ?? items["c"];
        return New(ArrivalKind.Campaign, campaign);
    }

    public static bool TryParse(string? value, out ArrivalInfo result)
    {
        result = default;
        if (value.IsNullOrEmpty())
            return false;

        var colonIndex = value.IndexOf(':');
        var prefix = colonIndex < 0 ? value : value[..colonIndex];
        var id = colonIndex < 0 ? null : value[(colonIndex + 1)..];
        if (!TryParseKind(prefix, out var kind) || New(kind, id) is not { } arrival)
            return false;

        result = arrival;
        return true;
    }

    public string Format()
        => Id.IsNullOrEmpty() ? FormatKind(Kind) : $"{FormatKind(Kind)}:{Id}";

    // Private methods

    private static bool IsValidId(string? id)
        => id is { Length: > 0 and <= MaxIdLength } && IdRegex().IsMatch(id);

    private static string FormatKind(ArrivalKind kind)
        => kind switch {
            ArrivalKind.Web => "web",
            ArrivalKind.Store => "store",
            ArrivalKind.Join => "join",
            ArrivalKind.User => "user",
            ArrivalKind.Campaign => "campaign",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static bool TryParseKind(string value, out ArrivalKind kind)
    {
        (var isParsed, kind) = value switch {
            "web" => (true, ArrivalKind.Web),
            "store" => (true, ArrivalKind.Store),
            "join" => (true, ArrivalKind.Join),
            "user" => (true, ArrivalKind.User),
            "campaign" => (true, ArrivalKind.Campaign),
            _ => (false, default),
        };
        return isParsed;
    }

    [GeneratedRegex("^[A-Za-z0-9_.@-]+$")]
    private static partial Regex IdRegex();
}
