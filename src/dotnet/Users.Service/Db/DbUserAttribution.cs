using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Users.Db;

[Table("UserAttributions")]
[Index(nameof(UserId), nameof(DedupeKey), IsUnique = true)]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public sealed class DbUserAttribution
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public AttributionOrigin Origin { get; set; }
    public bool IsInitial { get; set; }
    public bool IsLatest { get; set; }

    public DateTime FirstSeenAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime LastSeenAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public string Platform { get; set; } = "";
    public string Source { get; set; } = "";
    public string Medium { get; set; } = "";
    public string Campaign { get; set; } = "";
    public string AdGroup { get; set; } = "";
    public string Term { get; set; } = "";
    public string KeywordId { get; set; } = "";
    public string MatchType { get; set; } = "";
    public string AdId { get; set; } = "";
    public ClickIdKind? ClickIdKind { get; set; }
    public string ClickId { get; set; } = "";
    public string Claim { get; set; } = "";
    public string InstallKind { get; set; } = "";
    public string Country { get; set; } = "";
    public string Raw { get; set; } = "";
    public string DedupeKey { get; set; } = "";

    public void UpdateFrom(AttributionDetails details)
    {
        Platform = details.Platform;
        Source = details.Source;
        Medium = details.Medium;
        Campaign = details.Campaign;
        AdGroup = details.AdGroup;
        Term = details.Term;
        KeywordId = details.KeywordId;
        MatchType = details.MatchType;
        AdId = details.AdId;
        ClickIdKind = details.ClickIdKind;
        ClickId = details.ClickId;
        Claim = details.Claim;
        InstallKind = details.InstallKind;
        Country = details.Country;
        DedupeKey = details.DedupeKey;
    }

    public UserAttribution ToModel()
    {
        var details = new AttributionDetails {
            Platform = Platform,
            Source = Source,
            Medium = Medium,
            Campaign = Campaign,
            AdGroup = AdGroup,
            Term = Term,
            KeywordId = KeywordId,
            MatchType = MatchType,
            AdId = AdId,
            ClickIdKind = ClickIdKind,
            ClickId = ClickId,
            Claim = Claim,
            InstallKind = InstallKind,
            Country = Country,
        };
        return new UserAttribution(
            Id, ActualChat.UserId.Parse(UserId), Origin, details,
            FirstSeenAt.ToMoment(), LastSeenAt.ToMoment(), IsInitial, IsLatest, Raw);
    }
}
