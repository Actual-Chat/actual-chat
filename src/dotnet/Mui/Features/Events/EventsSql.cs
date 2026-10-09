namespace ActualChat.Mui;

public static class EventsSql
{
    // A payload-less row is a commit marker ("~op-" + operation uuid) that Fusion writes to verify commits
    public const string Summary = """
        select count(*) as total,
            count(*) filter (where value_data is null and value_json is null) as markers,
            count(*) filter (where state = 0 and delay_until <= now()) as pending,
            count(*) filter (where state = 0 and delay_until > now()) as delayed,
            extract(epoch from now() - min(delay_until)
                filter (where state = 0 and delay_until <= now())) as oldest_pending_s,
            count(*) filter (where logged_at > now() - interval '60 seconds'
                and (value_data is not null or value_json is not null)) as created_60s,
            count(*) filter (where logged_at > now() - interval '5 minutes'
                and (value_data is not null or value_json is not null)) as created_5m
        from _events
        """;

    // value_data is MessagePack and starts with the string "Namespace.Type, Assembly": 0xD9 = str8,
    // 0xDA = str16, 0xA0..0xBF = fixstr. LATIN1 decoding never fails; type names are ASCII anyway.
    public const string Types = """
        select type,
            count(*) filter (where logged_at > now() - interval '5 minutes') as created_5m,
            count(*) filter (where state = 0 and delay_until <= now()) as pending,
            count(*) filter (where state = 0 and delay_until > now()) as delayed
        from (
            select logged_at, state, delay_until,
                case
                    when value_data is not null then coalesce(
                        regexp_replace(split_part(case
                            when get_byte(value_data, 0) = 217
                                then convert_from(substring(value_data from 3 for get_byte(value_data, 1)), 'LATIN1')
                            when get_byte(value_data, 0) = 218
                                then convert_from(substring(value_data from 4
                                    for get_byte(value_data, 1) * 256 + get_byte(value_data, 2)), 'LATIN1')
                            when get_byte(value_data, 0) between 160 and 191
                                then convert_from(substring(value_data from 2
                                    for get_byte(value_data, 0) - 160), 'LATIN1')
                        end, ',', 1), '^.*\.', ''),
                        '(unrecognized)')
                    when value_json is not null then '(json payload)'
                    else '(commit marker)'
                end as type
            from _events
        ) e
        group by type
        order by created_5m desc, pending desc, type
        limit 100
        """;
}
