using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using ActualLab.Generators;
using ActualLab.Versioning;

namespace ActualChat.Invite.Db;

[Table("Invites")]
[Index(nameof(SearchKey), nameof(Remaining))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbInvite : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    public static readonly RandomStringGenerator IdGenerator = new(10, Alphabet.AlphaNumeric);

    // The DetailsJson column stores the leaf-specific payload as JSON in the
    // wrapper-style legacy shape (LegacyInviteDetails / LegacyInviteDetailsOption).
    // This preserves the on-disk format - existing rows deserialize without migration -
    // while the in-memory model uses the new Invite hierarchy.
    private static ITextSerializer<LegacyInviteDetails> LegacyDetailsSerializer { get; } =
        Serializers.SystemJson.ToTyped<LegacyInviteDetails>();

    public DbInvite() { }
    public DbInvite(Invite invite) => UpdateFrom(invite);

    [DbKey] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }

    public string SearchKey { get; set; } = "";
    public int Remaining { get; set; }
    public string CreatedBy { get; set; } = "";

    public DateTime CreatedAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime ExpiresOn {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public string DetailsJson { get; set; } = "";

    public Invite ToModel()
    {
        var legacyDetails = DetailsJson.IsNullOrEmpty() ? null : LegacyDetailsSerializer.Read(DetailsJson);
        Invite invite = legacyDetails?.Option switch {
            LegacyChatInviteOption chat => new ChatInvite(Id, Version) { ChatId = chat.ChatId },
            LegacyPlaceInviteOption place => new PlaceInvite(Id, Version) { PlaceId = place.PlaceId },
            LegacyUserInviteOption => new UserInvite(Id, Version),
            null => new UserInvite(Id, Version), // Defensive — pre-typed rows
            _ => throw StandardError.Internal($"Unknown invite details option: {legacyDetails.Option.GetType().Name}"),
        };
        return invite with {
            Remaining = Remaining,
            ExpiresOn = ExpiresOn,
            CreatedAt = CreatedAt,
            CreatedBy = CreatedBy,
        };
    }

    public void UpdateFrom(Invite model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id);
        model.RequireVersion();

        Id = id;
        Version = model.Version;
        Remaining = model.Remaining;
        ExpiresOn = model.ExpiresOn;
        CreatedAt = model.CreatedAt;
        CreatedBy = model.CreatedBy;

        SearchKey = model.GetSearchKey();
        DetailsJson = LegacyDetailsSerializer.Write(ToLegacyDetails(model));
    }

    private static LegacyInviteDetails ToLegacyDetails(Invite model) => model switch {
        ChatInvite chat => new LegacyInviteDetails { Option = new LegacyChatInviteOption(chat.ChatId) },
        PlaceInvite place => new LegacyInviteDetails { Option = new LegacyPlaceInviteOption(place.PlaceId) },
        UserInvite => new LegacyInviteDetails { Option = new LegacyUserInviteOption() },
        _ => throw StandardError.Internal($"Unknown invite type: {model.GetType().Name}"),
    };
}
