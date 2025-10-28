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

    public long StartTrackingEntryLid { get; set; }

    public long Top1EntryLid { get; set; }
    public string Top1UserId { get; set; } = "";

    public long Top2EntryLid { get; set; }
    public string Top2UserId { get; set; } = "";

    string IHasId<string>.Id => ChatId;

    public UserReadPosition[] GetTopReadPositions()
    {
        var buffer = ArrayBuffer<UserReadPosition>.Lease(true, 2);
        try {
            if (Top1EntryLid > 0)
                buffer.Add(new UserReadPosition(UserId.Parse(Top1UserId), Top1EntryLid));
            if (Top2EntryLid > 0)
                buffer.Add(new UserReadPosition(UserId.Parse(Top2UserId), Top2EntryLid));
            return buffer.ToArray();
        }
        finally {
            buffer.Release();
        }
    }
}
