using ActualChat.Integrations.Anthropic;
using Cysharp.Text;

namespace ActualChat.MLSearch.Indexing.ChatContent;

internal interface IDialogFragmentAnalyzer
{
    Task<int> ChooseMoreProbableDialog(string[] dialogs);
    Task<Option<bool>> IsDialogAboutTheSameTopic(string dialog);
}

internal partial class DialogFragmentAnalyzer(
    DialogFragmentAnalyzer.Options options,
    ILogger<DialogFragmentAnalyzer> log,
    IPromptUtils promptUtils,
    IAnthropicClient anthropicClient) : IDialogFragmentAnalyzer
{
    public record Options
    {
        public static Options Default { get; set; } = new();
        public bool IsDiagnosticsEnabled { get; init; }
    }

    private ILogger Log { get; } = log;
    private bool IsDiagnosticsEnabled => options.IsDiagnosticsEnabled;

    public async Task<int> ChooseOption(string phrase, string[] fragments)
    {
        using var sb = ZString.CreateStringBuilder();
        sb.Append("Choose more relevant continuation to the phrase below from given options.");
        if (!IsDiagnosticsEnabled)
            sb.Append(" Answer with option name only.");
        sb.AppendLine();
        sb.AppendLine("Phrase: " + phrase);

        var fragmentWithKeys = new Dictionary<string, int>(StringComparer.Ordinal);
        var alphabet = Alphabet.AlphaLower;
        var i = 0;
        foreach (var fragment in fragments) {
            var letter = alphabet.Symbols[i];
            var optionKey = new string(letter, 4);
            fragmentWithKeys.Add(optionKey, i);
            var keyedFragment = optionKey + ") " + fragment;
            sb.AppendLine(keyedFragment);
            i++;
        }
        var prompt = sb.ToString();

        var (isOk, reply) = await GetReply(prompt).ConfigureAwait(false);
        if (!isOk)
            return int.MinValue;

        var result = fragmentWithKeys
            .Select(pair => new { OptionIndex = pair.Value, AnswerIndex = reply.IndexOf(pair.Key, StringComparison.Ordinal) })
            .Where(c => c.AnswerIndex > -1)
            .OrderBy(c => c.AnswerIndex)
            .Select(c => c.OptionIndex)
            .FirstOrDefault(-1);
        return result;
    }

    public async Task<int> ChooseMoreProbableDialog(string[] dialogs)
    {
        using var sb = ZString.CreateStringBuilder();
        var dialogWithKeys = new Dictionary<string, int>(StringComparer.Ordinal);
        var alphabet = Alphabet.AlphaLower;
        var i = 0;
        foreach (var dialog in dialogs) {
            var letter = alphabet.Symbols[i];
            var optionKey = new string(letter, 4).ToUpperInvariant();
            dialogWithKeys.Add(optionKey, i);
            var optionKeyFull = $"Dialog {optionKey}.";
            sb.AppendLine(optionKeyFull);
            sb.AppendLine(dialog);
            i++;
        }
        var dialogFragments = sb.ToString();

        var prompt = promptUtils.BuildPrompt(
            IsDiagnosticsEnabled ? Prompts.ChooseMoreProbableDialogWithAnalysis : Prompts.ChooseMoreProbableDialog,
            new Dictionary<string, string>(StringComparer.Ordinal) {
                { "DIALOG_FRAGMENTS", dialogFragments },
            });

        var (isOk, reply) = await GetReply(prompt).ConfigureAwait(false);
        if (!isOk)
            return int.MinValue;

        var decision = promptUtils.GetXmlTagValue(reply, "decision");

        var result = dialogWithKeys
            .Select(pair => new { OptionIndex = pair.Value, AnswerIndex = decision.IndexOf(pair.Key, StringComparison.Ordinal) })
            .Where(c => c.AnswerIndex > -1)
            .OrderBy(c => c.AnswerIndex)
            .Select(c => c.OptionIndex)
            .FirstOrDefault(-1);
        return result;
    }

    public async Task<Option<bool>> IsDialogAboutTheSameTopic(string dialog)
    {
        var prompt = promptUtils.BuildPrompt(
            IsDiagnosticsEnabled ? Prompts.IsDialogAboutTheSameTopicWithAnalysis : Prompts.IsDialogAboutTheSameTopic,
            new Dictionary<string, string>(StringComparer.Ordinal) {
                { "CHAT_FRAGMENT", dialog },
            });

        var (isOk, reply) = await GetReply(prompt).ConfigureAwait(false);
        if (!isOk)
            return Option.None<bool>();

        var decision = promptUtils.GetXmlTagValue(reply, "decision");

        return Option.Some(decision.Contains("yes", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<(bool isOk, string reply)> GetReply(string prompt, [CallerMemberName] string caller = "")
    {
        try {
            var reply = await anthropicClient.Execute(prompt, default).ConfigureAwait(false);
            if (IsDiagnosticsEnabled)
                Log.LogInformation("{Caller} succeeded to get reply. Reply: '{Reply}', Prompt: '{Prompt}'", caller, reply, prompt);
            return (true, reply);
        }
        catch (Exception e) {
            var isDebugEnabled = Log.IsEnabled(LogLevel.Debug);
            if (isDebugEnabled)
                Log.LogError(e, "{Caller} failed to get response on prompt: '{Prompt}'", caller, prompt);
            else
                Log.LogError(e, "{Caller} failed to get response on prompt", caller);
            return (false, "");
        }
    }
}
