using System.Text;

namespace BlazorContextMenu;

internal static class Helpers
{
    public static string AppendCssClasses(params string[] cssClasses)
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
