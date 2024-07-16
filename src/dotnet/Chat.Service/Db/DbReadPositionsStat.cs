using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualLab.Versioning;

namespace ActualChat.Chat.Db;

[Table("DbReadPositionsStat")]
public class DbReadPositionsStat : IHasId<string>, IHasVersion<long>
{
    public DbReadPositionsStat() { }

    [Key] public string ChatId { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public long StartTrackingEntryLid { get; set; } = 0;

    public long Top1EntryLid { get; set; } = 0;
    public string Top1UserId { get; set; } = "";

    public long Top2EntryLid { get; set; } = 0;
    public string Top2UserId { get; set; } = "";

    string IHasId<string>.Id => ChatId;

    public ApiArray<UserReadPosition> GetTopReadPositions()
    {
        var result = ApiArray<UserReadPosition>.Empty;
        if (Top1EntryLid > 0)
            result = result.Add(new UserReadPosition(new UserId(Top1UserId), Top1EntryLid));
        if (Top2EntryLid > 0)
            result = result.Add(new UserReadPosition(new UserId(Top2UserId), Top2EntryLid));
        return result;
    }
}
