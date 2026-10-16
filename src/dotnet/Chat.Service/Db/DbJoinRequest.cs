using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualLab.Versioning;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Db;

[Table("JoinRequests")]
[Index(nameof(ChatId), nameof(Status))]
[Index(nameof(UserId), nameof(ChatId))]
[Index(nameof(ParentId))]
[Index(nameof(Status), nameof(ExpiresAt))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public sealed class DbJoinRequest : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [DbKey] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public string ChatId { get; set; } = "";
    public string UserId { get; set; } = "";
    public JoinRequestStatus Status { get; set; }
    public string? ParentId { get; set; }
    public string? InviteId { get; set; }
    public bool WasExcluded { get; set; }
    public string? DecidedBy { get; set; }

    public DateTime CreatedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime ExpiresAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime? DecidedAt {
        get => field?.DefaultKind(DateTimeKind.Utc);
        set => field = value?.DefaultKind(DateTimeKind.Utc);
    }

    public DbJoinRequest() { }
    public DbJoinRequest(JoinRequest model) => UpdateFrom(model);

    public JoinRequest ToModel()
        => new(JoinRequestId.Parse(Id), Version) {
            ChatId = ActualChat.ChatId.Parse(ChatId),
            UserId = ActualChat.UserId.Parse(UserId),
            Status = Status,
            ParentId = JoinRequestId.ParseNullable(ParentId),
            InviteId = InviteId ?? Symbol.Empty,
            WasExcluded = WasExcluded,
            CreatedAt = CreatedAt.ToMoment(),
            ExpiresAt = ExpiresAt.ToMoment(),
            DecidedAt = DecidedAt?.ToMoment(),
            DecidedBy = ActualChat.UserId.ParseNullable(DecidedBy),
        };

    public void UpdateFrom(JoinRequest model)
    {
        this.RequireSameOrEmptyId(model.Id.Value);
        model.RequireVersion();

        Id = model.Id.Value;
        Version = model.Version;
        ChatId = model.ChatId.Value;
        UserId = model.UserId.Value;
        Status = model.Status;
        ParentId = model.ParentId?.Value;
        InviteId = model.InviteId.IsEmpty ? null : model.InviteId.Value;
        WasExcluded = model.WasExcluded;
        CreatedAt = model.CreatedAt;
        ExpiresAt = model.ExpiresAt;
        DecidedAt = model.DecidedAt?.ToDateTime();
        DecidedBy = model.DecidedBy?.Value;
    }
}
