using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualChat.Hashing;
using ActualLab.Versioning;

namespace ActualChat.Users.Db;

[Table("UserVoices")]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbUserVoice : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [DbKey] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }
    public string SampleHash { get; set; } = "";
    public string SonioxVoiceId { get; set; } = "";
    public UserVoiceStatus Status { get; set; }

    public DateTime? FailedUntil {
        get => field?.DefaultKind(DateTimeKind.Utc);
        set => field = value?.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime LastUsedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime CreatedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime ModifiedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DbUserVoice() { }
    public DbUserVoice(UserVoice model) => UpdateFrom(model);

    public UserVoice ToModel()
        => new(UserId.Parse(Id), Version) {
            SampleHash = new HashString(SampleHash),
            SonioxVoiceId = SonioxVoiceId,
            Status = Status,
            FailedUntil = FailedUntil?.ToMoment(),
            LastUsedAt = LastUsedAt.ToMoment(),
            CreatedAt = CreatedAt.ToMoment(),
            ModifiedAt = ModifiedAt.ToMoment(),
        };

    public void UpdateFrom(UserVoice model)
    {
        this.RequireSameOrEmptyId(model.UserId.Value);
        model.RequireVersion();

        Id = model.UserId.Value;
        Version = model.Version;
        SampleHash = model.SampleHash;
        SonioxVoiceId = model.SonioxVoiceId;
        Status = model.Status;
        FailedUntil = model.FailedUntil?.ToDateTime();
        LastUsedAt = model.LastUsedAt.ToDateTime();
        CreatedAt = model.CreatedAt.ToDateTime();
        ModifiedAt = model.ModifiedAt.ToDateTime();
    }
}
