using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using ActualLab.Versioning;

namespace ActualChat.Users.Db;

[Table("_Sessions")]
[Index(nameof(CreatedAt))]
[Index(nameof(LastSeenAt))]
[Index(nameof(ExpiresAt))]
[Index(nameof(UserId))]
[Index(nameof(IPAddress))]
public class DbSession : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private const string GuestIdKey = "GuestId";

    [DbKey, StringLength(256)]
    public string Id { get; set; } = "";

    [ConcurrencyCheck]
    public long Version { get; set; }

    public DateTime CreatedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }
    public DateTime LastSeenAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }
    public DateTime ExpiresAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public string IPAddress { get; set; } = "";
    public string Description { get; set; } = "";

    public string AuthenticatedIdentity { get; set; } = "";
    public string? UserId { get; set; }

    public string OptionsJson {
        get => Options.ToJson();
        set => Options = ReadOptions(value);
    }

    [NotMapped]
    public MetadataBag Options { get; set; }

    [NotMapped]
    public bool IsActive => ExpiresAt >= DateTime.UtcNow;

    public SessionInfoFull ToModel()
        => new(new Session(Id)) {
            Version = Version,
            IsActive = IsActive,
            CreatedAt = CreatedAt,
            LastSeenAt = LastSeenAt,
            ExpiresAt = ExpiresAt,
            IPAddress = IPAddress,
            Description = Description,
            GuestId = GetGuestId(),

            // Authentication
            AuthenticatedIdentity = IsActive ? AuthenticatedIdentity : "",
            UserId = IsActive ? ActualChat.UserId.ParseNullable(UserId) : null,
        };

    public void UpdateFrom(SessionInfoFull source, VersionGenerator<long> versionGenerator)
    {
        if (new Session(Id) != source.Session)
            throw new ArgumentOutOfRangeException(nameof(source));
        if (!IsActive)
            throw StandardError.Unavailable($"This {source.Session.Kind.ToReadable()} is expired.");

        Version = versionGenerator.NextVersion(Version);
        LastSeenAt = source.LastSeenAt;
        ExpiresAt = source.ExpiresAt;
        IPAddress = source.IPAddress;
        Description = source.Description;

        AuthenticatedIdentity = source.AuthenticatedIdentity;
        UserId = source.UserId?.Value;

        if (GetGuestId() is null)
            Options = Options.Set(GuestIdKey, ActualChat.UserId.NewGuest().Value);
    }

    // Private methods

    private UserId? GetGuestId()
        => Options[GuestIdKey] is string value
            && ActualChat.UserId.TryParse(value, out var guestId)
            && guestId.IsGuest
            ? guestId
            : null;

    private static MetadataBag ReadOptions(string? json)
    {
        try {
            var options = MetadataBagJson.FromJson(json);
            return options[GuestIdKey] is null ? ReadLegacyOptions(json) : options;
        }
        catch {
            return ReadLegacyOptions(json);
        }
    }

    private static MetadataBag ReadLegacyOptions(string? json)
    {
        // Lifting the only entry these rows ever held out by token leaves their "$type" unresolved
        try {
            var key = typeof(GuestIdOption).ToIdentifierSymbol().Value;
            var guestId = JObject.Parse(json!)["Items"]?[key]?["GuestId"]?.Value<string>();
            return ActualChat.UserId.TryParse(guestId, out var userId) && userId.IsGuest
                ? MetadataBag.Empty.Set(GuestIdKey, userId.Value)
                : MetadataBag.Empty;
        }
        catch {
            return MetadataBag.Empty;
        }
    }
}
