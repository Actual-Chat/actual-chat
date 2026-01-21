using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using ActualLab.Versioning;

namespace ActualChat.Users.Db;

[Table("_Sessions")]
[Index(nameof(CreatedAt), nameof(IsSignOutForced))]
[Index(nameof(LastSeenAt), nameof(IsSignOutForced))]
[Index(nameof(UserId), nameof(IsSignOutForced))]
[Index(nameof(IPAddress), nameof(IsSignOutForced))]
public class DbSessionInfo : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private NewtonsoftJsonSerialized<ImmutableOptionSet> _options = ImmutableOptionSet.Empty;

    [Key, StringLength(256)]
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

    public string IPAddress { get; set; } = "";
    public string UserAgent { get; set; } = "";

    // Authentication
    public string AuthenticatedIdentity { get; set; } = "";
    public string? UserId { get; set; }
    public bool IsSignOutForced { get; set; }

    // Options
    public string OptionsJson {
        get => _options.Data;
        set => _options = value;
    }

    [NotMapped]
    public ImmutableOptionSet Options {
        get => _options.Value;
        set => _options = value;
    }

    public SessionInfo ToModel(ILogger? log = null)
    {
        try {
            return ToModelCore();
        }
        catch (JsonSerializationException e) {
            log?.LogError(e, "SessionInfo.Options are incompatible with the current codebase, resetting them");
            Options = ImmutableOptionSet.Empty;
            return ToModelCore();
        }
    }

    private SessionInfo ToModelCore()
    {
        var session = new Session(Id);
        var now = CoarseSystemClock.Instance.Now;
        var result = IsSignOutForced
            ? new SessionInfo(session, now) {
                SessionHash = session.Hash,
                IsSignOutForced = true,
            }
            : new SessionInfo(now) {
                SessionHash = session.Hash,
                Version = Version,
                CreatedAt = CreatedAt,
                LastSeenAt = LastSeenAt,
                IPAddress = IPAddress,
                UserAgent = UserAgent,
                Options = Options,

                // Authentication
                AuthenticatedIdentity = AuthenticatedIdentity,
                UserId = UserId ?? "",
                IsSignOutForced = IsSignOutForced,
            };
        return result;
    }

    public void UpdateFrom(SessionInfo source, VersionGenerator<long> versionGenerator)
    {
        var session = new Session(Id);
        if (!Equals(session.Hash, source.SessionHash))
            throw new ArgumentOutOfRangeException(nameof(source));
        if (IsSignOutForced)
            throw StandardError.Unauthorized("Session unavailable.");

        Version = versionGenerator.NextVersion(Version);
        LastSeenAt = source.LastSeenAt;
        IPAddress = source.IPAddress;
        UserAgent = source.UserAgent;
        Options = source.Options;

        AuthenticatedIdentity = source.AuthenticatedIdentity;
        UserId = source.UserId.IsNullOrEmpty() ? null : source.UserId;
        IsSignOutForced = source.IsSignOutForced;

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
}
