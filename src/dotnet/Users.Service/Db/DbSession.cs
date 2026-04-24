using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
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
    private NewtonsoftJsonSerialized<ImmutableOptionSet> _options = ImmutableOptionSet.Empty;

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
        get => _options.Data;
        set => _options = value;
    }

    [NotMapped]
    public ImmutableOptionSet Options {
        get => _options.Value;
        set => _options = value;
    }

    [NotMapped]
    public bool IsActive => ExpiresAt >= DateTime.UtcNow;

    public SessionInfoFull ToModel(ILogger? log = null)
    {
        try {
            return ToModelCore();
        }
        catch (JsonSerializationException e) {
            log?.LogError(e, "SessionInfoFull.Options are incompatible with the current codebase, resetting them");
            Options = ImmutableOptionSet.Empty;
            return ToModelCore();
        }
    }

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
        Options = source.Options;

        AuthenticatedIdentity = source.AuthenticatedIdentity;
        UserId = source.UserId?.Value;

        // Ensure guestId is set
        GuestIdOption? guestIdOption = null;
        try {
            guestIdOption = Options.Get<GuestIdOption>();
        }
        catch {
            // Intended: GuestId type was changed, so it might throw an error
        }

        var guestId = guestIdOption?.GuestId;
        if (guestId?.IsGuest != true) {
            guestId = ActualChat.UserId.NewGuest();
            guestIdOption = new GuestIdOption(guestId);
            Options = Options.Set(guestIdOption);
        }
    }

    // Private methods

    private SessionInfoFull ToModelCore()
    {
        var isActive = IsActive;
        return new SessionInfoFull(new Session(Id)) {
            Version = Version,
            IsActive = isActive,
            CreatedAt = CreatedAt,
            LastSeenAt = LastSeenAt,
            ExpiresAt = ExpiresAt,
            IPAddress = IPAddress,
            Description = Description,
            Options = Options,

            // Authentication
            AuthenticatedIdentity = isActive ? AuthenticatedIdentity : "",
            UserId = isActive ? ActualChat.UserId.ParseNullable(UserId) : null,
        };
    }
}
