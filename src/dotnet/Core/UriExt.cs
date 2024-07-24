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
        => HttpUtility.ParseQueryString(uri.Query);

    public static Uri DropQueryItem(this Uri uri, string key)
    {
        var b = new UriBuilder(uri);
        var query = uri.GetQueryCollection();
        query.Remove(key);
        b.Query = RenderQuery(query);
        return b.Uri;
    }

    private static string RenderQuery(NameValueCollection parms)
    {
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        for (int i = 0; i < parms.Count; i++) {
            if (sb.Length > 0)
                sb.Append('&');
            var key = parms.Keys[i];
            sb.Append(key);
            sb.Append('=');
            sb.Append(parms[key]);
        }
        return sb.ToStringAndRelease();
    }
}
