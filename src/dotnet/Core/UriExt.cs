using System.Collections.Specialized;
using System.Web;

namespace ActualChat;

public static class UriExt
{
    public static string WithoutFragment(this Uri uri)
        => uri.Fragment.IsNullOrEmpty()
            ? uri.AbsoluteUri
            : uri.AbsoluteUri[..uri.AbsoluteUri.OrdinalLastIndexOf(uri.Fragment)];

    public static NameValueCollection GetQueryCollection(this Uri uri)
        => GetQueryCollection(uri.Query);

    public static NameValueCollection GetQueryCollection(string query)
        => HttpUtility.ParseQueryString(query);

    public static Uri DropQueryItem(this Uri uri, string key)
    {
        var b = new UriBuilder(uri);
        var query = uri.GetQueryCollection();
        query.Remove(key);
        b.Query = RenderQuery(query);
        return b.Uri;
    }

    private static string RenderQuery(NameValueCollection @params)
    {
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        for (int i = 0; i < @params.Count; i++) {
            if (sb.Length > 0)
                sb.Append('&');
            var key = @params.Keys[i];
            sb.Append(key);
            sb.Append('=');
            sb.Append(@params[key]);
        }
        return sb.ToStringAndRelease();
    }
}
