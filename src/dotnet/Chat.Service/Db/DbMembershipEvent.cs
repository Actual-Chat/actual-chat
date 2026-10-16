using System.ComponentModel.DataAnnotations.Schema;
using ActualLab.Generators;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Db;

[Table("MembershipEvents")]
[Index(nameof(ChatId), nameof(CreatedAt))]
[Index(nameof(UserId))]
[Index(nameof(InviteId))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public sealed class DbMembershipEvent : IHasId<string>
{
    private static readonly RandomStringGenerator IdGenerator = new(16, Alphabet.AlphaNumeric);

    [DbKey] public string Id { get; set; } = null!;

    public string ChatId { get; set; } = "";
    public string UserId { get; set; } = "";
    public string? AuthorId { get; set; }
    public MembershipEventKind Kind { get; set; }
    public MembershipMethod Method { get; set; }
    public string? ActorId { get; set; }
    public string? InviteId { get; set; }
    public string? RequestId { get; set; }

    public DateTime CreatedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public static string NewId()
        => IdGenerator.Next();

    public DbMembershipEvent() { }
    public DbMembershipEvent(MembershipEvent model) => UpdateFrom(model);

    public MembershipEvent ToModel()
        => new(Id, ActualChat.ChatId.Parse(ChatId), ActualChat.UserId.Parse(UserId), Kind, Method) {
            AuthorId = ActualChat.AuthorId.ParseNullable(AuthorId),
            ActorId = ActualChat.UserId.ParseNullable(ActorId),
            InviteId = InviteId ?? Symbol.Empty,
            RequestId = JoinRequestId.ParseNullable(RequestId),
            CreatedAt = CreatedAt.ToMoment(),
        };

    public void UpdateFrom(MembershipEvent model)
    {
        Id = model.Id;
        ChatId = model.ChatId.Value;
        UserId = model.UserId.Value;
        AuthorId = model.AuthorId?.Value;
        Kind = model.Kind;
        Method = model.Method;
        ActorId = model.ActorId?.Value;
        InviteId = model.InviteId.IsEmpty ? null : model.InviteId.Value;
        RequestId = model.RequestId?.Value;
        CreatedAt = model.CreatedAt;
    }
}
