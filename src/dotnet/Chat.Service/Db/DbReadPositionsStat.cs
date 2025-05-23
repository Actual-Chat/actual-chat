using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualLab.Versioning;

namespace ActualChat.Chat.Db;

[Table("DbReadPositionsStat")]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbReadPositionsStat : IHasId<string>, IHasVersion<long>
{
    [Key] public string ChatId { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public long StartTrackingEntryLid { get; set; } = 0;

    public long Top1EntryLid { get; set; } = 0;
    public string Top1UserId { get; set; } = "";

    public long Top2EntryLid { get; set; } = 0;
    public string Top2UserId { get; set; } = "";

    string IHasId<string>.Id => ChatId;

    public UserReadPosition[] GetTopReadPositions()
    {
        var result = Array.Empty<UserReadPosition>();
        if (Top1EntryLid > 0)
            result = result.With(new UserReadPosition(UserId.Parse(Top1UserId), Top1EntryLid));
        if (Top2EntryLid > 0)
            result = result.With(new UserReadPosition(UserId.Parse(Top2UserId), Top2EntryLid));
        return result;
    }
}
