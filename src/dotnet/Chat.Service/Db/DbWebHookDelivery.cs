using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualChat.WebHooks;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Db;

[Table("WebHookDeliveries")]
[Index(nameof(WebHookId), nameof(Seq))]
[Index(nameof(Status), nameof(NextAttemptAt))]
[Index(nameof(CreatedAt))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbWebHookDelivery : IHasId<string>
{
    [DbKey] public string Id { get; set; } = null!;

    public string WebHookId { get; set; } = "";
    public long Seq { get; set; }
    public string EventType { get; set; } = "";
    public WebHookDeliveryStatus Status { get; set; }
    public int Attempts { get; set; }

    public DateTime? NextAttemptAt {
        get => field?.DefaultKind(DateTimeKind.Utc);
        set => field = value?.DefaultKind(DateTimeKind.Utc);
    }

    public int? LastStatusCode { get; set; }
    public string? LastError { get; set; }
    public int? LastLatencyMs { get; set; }

    public DateTime CreatedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime? CompletedAt {
        get => field?.DefaultKind(DateTimeKind.Utc);
        set => field = value?.DefaultKind(DateTimeKind.Utc);
    }

    public string Payload { get; set; } = "";

    public DbWebHookDelivery() { }
    public DbWebHookDelivery(WebHookDelivery model) => UpdateFrom(model);

    public WebHookDelivery ToModel()
        => new(Id, ActualChat.WebHookId.Parse(WebHookId)) {
            Seq = Seq,
            EventType = EventType,
            Status = Status,
            Attempts = Attempts,
            NextAttemptAt = NextAttemptAt?.ToMoment(),
            LastStatusCode = LastStatusCode,
            LastError = LastError,
            LastLatencyMs = LastLatencyMs,
            CreatedAt = CreatedAt.ToMoment(),
            CompletedAt = CompletedAt?.ToMoment(),
        };

    public void UpdateFrom(WebHookDelivery model)
    {
        Id = model.Id;
        WebHookId = model.WebHookId.Value;
        Seq = model.Seq;
        EventType = model.EventType;
        Status = model.Status;
        Attempts = model.Attempts;
        NextAttemptAt = model.NextAttemptAt?.ToDateTime();
        LastStatusCode = model.LastStatusCode;
        LastError = model.LastError;
        LastLatencyMs = model.LastLatencyMs;
        CreatedAt = model.CreatedAt;
        CompletedAt = model.CompletedAt?.ToDateTime();
    }
}
