using System.Text;

namespace ActualChat.UI.Blazor;

public static class CssClasses
{
    public static string Concat(params string?[] cssClasses)
    {
        var builder = new StringBuilder();
        foreach(var cl in cssClasses) {
            if (string.IsNullOrEmpty(cl))
                continue;
            builder.Append(cl);
            builder.Append(" ");
        }
        return builder.ToString();
    }
}
