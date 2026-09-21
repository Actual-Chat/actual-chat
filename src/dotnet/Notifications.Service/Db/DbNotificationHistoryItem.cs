using System.ComponentModel.DataAnnotations.Schema;
using ActualChat.Db;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ActualChat.Notifications.Db;

// Id is "{NotificationId}:{SentAt ticks}", so an at-least-once redelivery of the same
// UserNotifiedEvent maps to the same row and the DoNothing conflict strategy drops it.
[Table("NotificationHistory")]
[Index(nameof(UserId), nameof(Seq))]
[Index(nameof(UserId), nameof(Kind), nameof(Seq))]
[Index(nameof(CreatedAt))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbNotificationHistoryItem : IHasId<string>, IRequirementTarget
{
    [DbKey] public string Id { get; set; } = null!;
    public long Seq { get; set; }
    public string UserId { get; set; } = "";
    public NotificationKind Kind { get; set; }
    public string ChatId { get; set; } = "";
    public long EntryLid { get; set; }
    public string AuthorId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";

    public DateTime SentAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime CreatedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public static string ComposeId(Notification notification)
        => $"{notification.Id.Value}:{notification.SentAt.EpochOffset.Ticks}";

    public DbNotificationHistoryItem() { }
    public DbNotificationHistoryItem(Notification notification, long seq, Moment createdAt)
    {
        Id = ComposeId(notification);
        Seq = seq;
        UserId = notification.UserId.Value;
        Kind = notification.Kind;
        ChatId = (notification as ChatNotification)?.ChatId.Value ?? "";
        EntryLid = notification switch {
            ChatEntryNotification n => n.EntryLid,
            ChatEntryRelatedNotification n => n.EntryLid,
            _ => 0,
        };
        AuthorId = (notification as ChatNotification)?.AuthorId?.Value ?? "";
        Title = notification.Title;
        Text = notification.Text;
        SentAt = notification.SentAt.ToDateTime();
        CreatedAt = createdAt.ToDateTime();
    }

    public NotificationHistoryItem ToModel()
    {
        var chatId = ChatId.IsNullOrEmpty() ? null : ActualChat.ChatId.Parse(ChatId);
        return new NotificationHistoryItem(Seq, Kind) {
            SentAt = SentAt.ToMoment(),
            ChatId = chatId,
            EntryId = chatId is not null && EntryLid > 0 ? ChatEntryId.New(chatId, EntryLid) : null,
            AuthorId = AuthorId.IsNullOrEmpty() ? null : ActualChat.AuthorId.Parse(AuthorId),
            Title = Title,
            Text = Text,
        };
    }

    // Nested types

    internal class EntityConfiguration : IEntityTypeConfiguration<DbNotificationHistoryItem>
    {
        public void Configure(EntityTypeBuilder<DbNotificationHistoryItem> builder)
            => builder.HasAnnotation(nameof(ConflictStrategy), ConflictStrategy.DoNothing);
    }
}
