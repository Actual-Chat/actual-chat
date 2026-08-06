using System.Text;

namespace ActualChat.Transcription;

// Deliberately over-provisioned: the source captures more authors and prefix entries than any single
// budget can use, and Render trims to fit. That keeps the expensive part - the chat reads - cacheable
// across transcribers whose policies differ.

/// <summary>
/// Chat-derived hints passed to a transcriber to improve recognition —
/// what was said just before and what the conversation is about.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record TranscriptionContext
{
    public static readonly TranscriptionContext None = new();
    private const string ParticipantsPrefix = "Participants: ";
    // The summary is the more influential half per token, but it must not crowd out the prefix.
    private const double SummaryBudgetShare = 0.4;

    [DataMember(Order = 0), Key(0)]
    public ApiArray<TranscriptionAuthor> Authors { get; init; } = ApiArray<TranscriptionAuthor>.Empty;
    [DataMember(Order = 1), Key(1)]
    public ApiArray<TranscriptionContextEntry> Prefix { get; init; } = ApiArray<TranscriptionContextEntry>.Empty;
    [DataMember(Order = 2), Key(2)]
    public string Summary { get; init; } = "";

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsNone => Prefix.Count == 0 && Summary.IsNullOrEmpty() && Authors.Count == 0;

    public TranscriptionContextText Render(int maxChars)
    {
        if (maxChars <= 0 || IsNone)
            return TranscriptionContextText.None;

        // The summary opens with the topic, so it's trimmed from the end.
        var summary = Summary.Truncate((int)(maxChars * SummaryBudgetShare));
        var textBudget = maxChars - summary.Length;
        var text = new StringBuilder();
        if (Authors.Count != 0) {
            var participants = string.Join(", ", Authors.Select(x => x.Name).Where(x => !x.IsNullOrEmpty()));
            if (participants.Length != 0 && ParticipantsPrefix.Length + participants.Length < textBudget) {
                text.Append(ParticipantsPrefix).Append(participants);
                textBudget -= text.Length;
            }
        }

        // The nearest entries matter most, so the prefix is filled backwards and emitted in order.
        var firstIndex = Prefix.Count;
        while (firstIndex > 0) {
            var entryLength = Prefix[firstIndex - 1].Text.Length + 1;
            if (entryLength > textBudget)
                break;

            textBudget -= entryLength;
            firstIndex--;
        }
        for (var i = firstIndex; i < Prefix.Count; i++)
            text.Append('\n').Append(Prefix[i].Text);

        return new TranscriptionContextText(summary, text.ToString().Trim());
    }
}
