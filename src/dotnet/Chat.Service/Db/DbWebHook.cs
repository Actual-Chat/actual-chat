using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualChat.WebHooks;
using ActualLab.Versioning;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Db;

[Table("WebHooks")]
[Index(nameof(ScopeId))]
[Index(nameof(CreatedBy))]
[Index(nameof(TokenHash), IsUnique = true)]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbWebHook : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [DbKey] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public WebHookScope Scope { get; set; }
    public string ScopeId { get; set; } = "";
    public WebHookKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string? CreatedBy { get; set; }

    public DateTime CreatedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime ModifiedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public bool IsEnabled { get; set; } = true;
    public WebHookDisabledReason DisabledReason { get; set; }
    public DateTime? LastActivityAt {
        get => field?.DefaultKind(DateTimeKind.Utc);
        set => field = value?.DefaultKind(DateTimeKind.Utc);
    }

    public string Url { get; set; } = "";
    public long Events { get; set; }
    public bool IncludeText { get; set; } = true;
    public string ChatIds { get; set; } = "";
    public bool SubscribeNotifications { get; set; }
    public string? CustomHeaderName { get; set; }
    public int ConsecutiveFailures { get; set; }
    public int? LastStatusCode { get; set; }
    public string? LastError { get; set; }

    public string? SecretProtected { get; set; }
    public string? PrevSecretProtected { get; set; }

    public DateTime? PrevSecretExpiresAt {
        get => field?.DefaultKind(DateTimeKind.Utc);
        set => field = value?.DefaultKind(DateTimeKind.Utc);
    }

    public string? CustomHeaderValueProtected { get; set; }
    public string? TokenHash { get; set; }

    public DbWebHook() { }
    public DbWebHook(WebHook model) => UpdateFrom(model);

    public WebHook ToModel()
        => new(WebHookId.Parse(Id), Version) {
            Scope = Scope,
            ScopeId = ScopeId,
            Kind = Kind,
            Name = Name,
            CreatedBy = UserId.ParseNullable(CreatedBy),
            CreatedAt = CreatedAt.ToMoment(),
            ModifiedAt = ModifiedAt.ToMoment(),
            IsEnabled = IsEnabled,
            DisabledReason = DisabledReason,
            LastActivityAt = LastActivityAt?.ToMoment(),
            Url = Url,
            Events = (WebHookEvents)Events,
            IncludeText = IncludeText,
            ChatIds = ChatIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(ChatId.Parse)
                .ToApiArray(),
            SubscribeNotifications = SubscribeNotifications,
            CustomHeaderName = CustomHeaderName,
            ConsecutiveFailures = ConsecutiveFailures,
            LastStatusCode = LastStatusCode,
            LastError = LastError,
        };

    public void UpdateFrom(WebHook model)
    {
        this.RequireSameOrEmptyId(model.Id.Value);
        model.RequireVersion();
        Id = model.Id.Value;
        Version = model.Version;
        Scope = model.Scope;
        ScopeId = model.ScopeId;
        Kind = model.Kind;
        Name = model.Name;
        CreatedBy = model.CreatedBy?.Value;
        CreatedAt = model.CreatedAt;
        ModifiedAt = model.ModifiedAt;
        IsEnabled = model.IsEnabled;
        DisabledReason = model.DisabledReason;
        LastActivityAt = model.LastActivityAt?.ToDateTime();
        Url = model.Url;
        Events = (long)model.Events;
        IncludeText = model.IncludeText;
        ChatIds = model.ChatIds.Select(c => c.Value).ToDelimitedString(",");
        SubscribeNotifications = model.SubscribeNotifications;
        CustomHeaderName = model.CustomHeaderName;
        ConsecutiveFailures = model.ConsecutiveFailures;
        LastStatusCode = model.LastStatusCode;
        LastError = model.LastError;
    }
}
