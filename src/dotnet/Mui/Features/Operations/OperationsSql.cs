namespace ActualChat.Mui;

public static class OperationsSql
{
    public const string Summary = """
        select count(*) as total,
            count(*) filter (where logged_at > now() - interval '60 seconds') as logged_60s,
            count(*) filter (where logged_at > now() - interval '5 minutes') as logged_5m,
            extract(epoch from now() - min(logged_at)) as oldest_s,
            extract(epoch from now() - max(logged_at)) as newest_s
        from _operations
        """;
}
