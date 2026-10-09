namespace ActualChat.Mui;

// One metric is a SQL scalar expression over a window; {from} and {to} are replaced with timestamp literals.
// MaxAge is how far back the data exists at all (null = unlimited): older windows show no value.
// Description says what the metric counts and where from, well enough to see which query makes each point.
public sealed record MuiMetric(
    string Key, string Title, string Sql, TimeSpan? MaxAge = null, string Description = "")
{
    private static readonly TimeSpan Tolerance = TimeSpan.FromMinutes(5);

    public bool IsAvailable(MuiWindow window, DateTime now)
        => MaxAge is not { } maxAge || window.From >= now - maxAge - Tolerance;
}

public static class MuiMetricSql
{
    public static string Build(IReadOnlyList<MuiMetric> metrics, MuiWindow window)
    {
        var columns = metrics.Select(m => $"({Apply(m.Sql, window)}) as \"{m.Key}\"");
        return "select " + string.Join(",\n       ", columns);
    }

    // The query that gives one point of one metric: runnable as is on the metric's database
    public static string BuildOne(MuiMetric metric, MuiWindow window)
        => Apply(metric.Sql, window);

    public static string Literal(DateTime value)
        => "timestamptz '" + value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'") + "'";

    // Relative change of the current value against a compared one; null when it can't be computed
    public static double? GetChangePercent(double current, double compared)
        => compared == 0 ? null : (current - compared) / Math.Abs(compared) * 100;

    // Private methods

    private static string Apply(string sql, MuiWindow window)
        => sql.Replace("{from}", Literal(window.From)).Replace("{to}", Literal(window.To));
}
