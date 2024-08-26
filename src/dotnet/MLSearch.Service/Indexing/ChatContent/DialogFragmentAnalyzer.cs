using System.Text;
using Anthropic.SDK;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;

namespace ActualChat.MLSearch.Indexing.ChatContent;

internal interface IDialogFragmentAnalyzer
{
    Task<int> ChooseMoreProbableDialog(string[] dialogs);
    Task<Option<bool>> IsDialogAboutTheSameTopic(string dialog);
}

internal class DialogFragmentAnalyzer(DialogFragmentAnalyzer.Options options, ILogger log) : IDialogFragmentAnalyzer
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
        var sb = new StringBuilder();
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
        var sb = new StringBuilder();
        sb.AppendLine("Which one of the dialogs below looks more probable?");
        if (!IsDiagnosticsEnabled)
            sb.Append(" Answer with option name only.");
        sb.AppendLine();

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
        var prompt = sb.ToString();

        var (isOk, reply) = await GetReply(prompt).ConfigureAwait(false);
        if (!isOk)
            return int.MinValue;

        var result = dialogWithKeys
            .Select(pair => new { OptionIndex = pair.Value, AnswerIndex = reply.IndexOf(pair.Key, StringComparison.Ordinal) })
            .Where(c => c.AnswerIndex > -1)
            .OrderBy(c => c.AnswerIndex)
            .Select(c => c.OptionIndex)
            .FirstOrDefault(-1);
        return result;
    }

    public async Task<Option<bool>> IsDialogAboutTheSameTopic(string dialog)
    {
        var sb = new StringBuilder();
        sb.Append("Say if the sentences below looks like they belong to the dialog about the same topic.");
        if (!IsDiagnosticsEnabled)
            sb.Append(" Answer with just Yes or No.");
        sb.AppendLine();
        sb.AppendLine(dialog);
        var prompt = sb.ToString();

        var (isOk, reply) = await GetReply(prompt).ConfigureAwait(false);
        if (!isOk)
            return Option.None<bool>();

        return Option.Some(reply.Contains("yes", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<(bool isOk, string reply)> GetReply(string prompt, [CallerMemberName] string caller = "")
    {
        var parameters = new MessageParameters {
            Messages = [new Message(RoleType.User, prompt)],
            MaxTokens = 1024,
            Model = AnthropicModels.Claude_v2_1,
            Stream = false,
            Temperature = 0.01m,
        };
        var client = new AnthropicClient();
        try {
            var response = await client.Messages.GetClaudeMessageAsync(parameters).ConfigureAwait(false);
            var reply = response.Message.ToString()!;
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
