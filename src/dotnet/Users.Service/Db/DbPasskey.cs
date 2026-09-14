using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users.Db;

[Table("Passkeys")]
[Index(nameof(UserId))]
public class DbPasskey : IHasId<string>
{
    // base64url credential id
    [DbKey] public string Id { get; set; } = "";
    public string UserId { get; set; } = "";
    public byte[] UserHandle { get; set; } = [];
    public byte[] PublicKey { get; set; } = [];
    public long SignCount { get; set; }
    public Guid Aaguid { get; set; }
    public string Transports { get; set; } = "";
    public bool IsBackupEligible { get; set; }
    public bool IsBackedUp { get; set; }
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }

    public DbPasskey() { }
    public DbPasskey(PasskeyCredential model) => UpdateFrom(model);

    public PasskeyCredential ToModel()
        => new(Id, ActualChat.UserId.Parse(UserId)) {
            UserHandle = UserHandle,
            PublicKey = PublicKey,
            SignCount = (uint)SignCount,
            Aaguid = Aaguid,
            Transports = Transports,
            IsBackupEligible = IsBackupEligible,
            IsBackedUp = IsBackedUp,
            Name = Name,
            CreatedAt = CreatedAt.ToMoment(),
            LastUsedAt = LastUsedAt?.ToMoment(),
        };

    public void UpdateFrom(PasskeyCredential model)
    {
        Id = model.Id;
        UserId = model.UserId.Value;
        UserHandle = model.UserHandle;
        PublicKey = model.PublicKey;
        SignCount = model.SignCount;
        Aaguid = model.Aaguid;
        Transports = model.Transports;
        IsBackupEligible = model.IsBackupEligible;
        IsBackedUp = model.IsBackedUp;
        Name = model.Name;
        CreatedAt = model.CreatedAt.ToDateTime();
        LastUsedAt = model.LastUsedAt?.ToDateTime();
    }
}
