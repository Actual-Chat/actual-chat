using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ActualLab.Versioning;
using Microsoft.EntityFrameworkCore;

namespace ActualChat.Chat.Db;

[Table("CoachConversations")]
[Index(nameof(ChatId), nameof(AuthorId))]
[SuppressMessage("ReSharper", "EntityFramework.ModelValidation.UnlimitedStringLength")]
public class DbCoachConversation : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    [DbKey] public string Id { get; set; } = null!;
    [ConcurrencyCheck] public long Version { get; set; }
    public string ChatId { get; set; } = "";
    public long StartEntryLid { get; set; }
    public string AuthorId { get; set; } = "";
    public string UserId { get; set; } = "";
    public long ConversationVersion { get; set; }

    public DateTime EndsAt {
        get => field.DefaultKind(DateTimeKind.Utc);
        set => field = value.DefaultKind(DateTimeKind.Utc);
    }

    public double OwnSpeechSeconds { get; set; }
    public double TotalSpeechSeconds { get; set; }
    public int OwnTurns { get; set; }
    public int TotalTurns { get; set; }
    public int Participants { get; set; }
    public double LongestMonologueSeconds { get; set; }
    public int Responses { get; set; }
    public double ResponseGapSeconds { get; set; }
    public int Interruptions { get; set; }

    public DbCoachConversation() { }
    public DbCoachConversation(CoachConversationAnalysis model) => UpdateFrom(model);

    public static string ComposeId(ConversationId conversationId, AuthorId authorId)
        => $"{conversationId}:{authorId}";

    public CoachConversationAnalysis ToModel()
        => new (ConversationId.New(ActualChat.ChatId.Parse(ChatId), StartEntryLid), ActualChat.AuthorId.Parse(AuthorId), Version) {
            UserId = ActualChat.UserId.Parse(UserId),
            ConversationVersion = ConversationVersion,
            EndsAt = EndsAt,
            OwnSpeechSeconds = OwnSpeechSeconds,
            TotalSpeechSeconds = TotalSpeechSeconds,
            OwnTurns = OwnTurns,
            TotalTurns = TotalTurns,
            Participants = Participants,
            LongestMonologueSeconds = LongestMonologueSeconds,
            Responses = Responses,
            ResponseGapSeconds = ResponseGapSeconds,
            Interruptions = Interruptions,
        };

    public void UpdateFrom(CoachConversationAnalysis model)
    {
        this.RequireSameOrEmptyId(ComposeId(model.Id, model.AuthorId));
        model.RequireVersion();

        Id = ComposeId(model.Id, model.AuthorId);
        Version = model.Version;
        ChatId = model.Id.ChatId.Value;
        StartEntryLid = model.Id.StartEntryLid;
        AuthorId = model.AuthorId.Value;
        UserId = model.UserId.Value;
        ConversationVersion = model.ConversationVersion;
        EndsAt = model.EndsAt;
        OwnSpeechSeconds = model.OwnSpeechSeconds;
        TotalSpeechSeconds = model.TotalSpeechSeconds;
        OwnTurns = model.OwnTurns;
        TotalTurns = model.TotalTurns;
        Participants = model.Participants;
        LongestMonologueSeconds = model.LongestMonologueSeconds;
        Responses = model.Responses;
        ResponseGapSeconds = model.ResponseGapSeconds;
        Interruptions = model.Interruptions;
    }
}
