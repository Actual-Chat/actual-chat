using System.Text;
using Anthropic.SDK;
using Anthropic.SDK.Constants;
using Anthropic.SDK.Messaging;
using Cysharp.Text;

namespace ActualChat.MLSearch.Indexing.ChatContent;

internal interface IDialogFragmentAnalyzer
{
    Task<int> ChooseMoreProbableDialog(string[] dialogs);
    Task<Option<bool>> IsDialogAboutTheSameTopic(string dialog);
}

internal partial class DialogFragmentAnalyzer(DialogFragmentAnalyzer.Options options, ILogger log) : IDialogFragmentAnalyzer
{
    public record Options
    {
        public static Options Default { get; set; } = new();

        public bool IsDiagnosticsEnabled { get; init; }
    }

    private readonly Dictionary<string, PromptTemplate> _promptTemplates = new(StringComparer.Ordinal);

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

        var prompt = BuildPrompt(
            IsDiagnosticsEnabled ? Prompts.ChooseMoreProbableDialogWithAnalysis : Prompts.ChooseMoreProbableDialog,
            new Dictionary<string, string>(StringComparer.Ordinal) {
                { "DIALOG_FRAGMENTS", dialogFragments },
            });

        var (isOk, reply) = await GetReply(prompt).ConfigureAwait(false);
        if (!isOk)
            return int.MinValue;

        var decision = GetXmlTagValue(reply, "decision");

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
        var prompt = BuildPrompt(
            IsDiagnosticsEnabled ? Prompts.IsDialogAboutTheSameTopicWithAnalysis : Prompts.IsDialogAboutTheSameTopic,
            new Dictionary<string, string>(StringComparer.Ordinal) {
                { "CHAT_FRAGMENT", dialog },
            });

        var (isOk, reply) = await GetReply(prompt).ConfigureAwait(false);
        if (!isOk)
            return Option.None<bool>();

        var decision = GetXmlTagValue(reply, "decision");

        return Option.Some(decision.Contains("yes", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<(bool isOk, string reply)> GetReply(string prompt, [CallerMemberName] string caller = "")
    {
        var parameters = new MessageParameters {
            Messages = [new Message(RoleType.User, prompt)],
            MaxTokens = 1024,
            Model = AnthropicModels.Claude3Haiku,
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

    private string BuildPrompt(string promptTemplate, IDictionary<string, string> variables)
    {
        var template = GetPromptTemplate(promptTemplate);
        var variableValues = new object[template.Variables.Length];
        for (var i = 0; i < template.Variables.Length; i++) {
            var variable = template.Variables[i];
            if (!variables.TryGetValue(variable, out var value))
                throw StandardError.Constraint($"Variable '{variable}' value was not provided.");
            variableValues[i] = value;
        }
        return string.Format(CultureInfo.InvariantCulture, template.FormatString, variableValues);
    }

    private PromptTemplate GetPromptTemplate(string template)
    {
        if (_promptTemplates.TryGetValue(template, out var promptTemplate))
            return promptTemplate;

        promptTemplate = BuildPromptTemplate(template);
        _promptTemplates.Add(template, promptTemplate);
        return promptTemplate;
    }

    private static PromptTemplate BuildPromptTemplate(string template)
    {
        var variables = new List<(string, int)>();
        var index = 0;
        while (true) {
            var varStart = template.IndexOf("{{", index, StringComparison.Ordinal);
            if (varStart < 0)
                break;

            var varEnd = template.IndexOf("}}", varStart, StringComparison.Ordinal);
            if (varEnd < 0)
                break;

            varEnd += 2;
            var variable = template.Substring(varStart, varEnd - varStart);
            variables.Add((variable, varStart));
            index = varEnd;
        }

        var formatString = template;
        for (int i = variables.Count - 1; i >= 0; i--) {
            var variable = variables[i];
            var startIndex = variable.Item2;
            formatString = formatString
                .Remove(startIndex, variable.Item1.Length)
                .Insert(startIndex, "{" + i.ToString(CultureInfo.InvariantCulture) +"}");
        }
        return new PromptTemplate(template, formatString, variables.Select(v => v.Item1.Substring(2, v.Item1.Length - 4)).ToArray());
    }

    private string GetXmlTagValue(string text, string tagName)
    {
        if (text.IsNullOrEmpty())
            return "";

        var openTag = "<" + tagName + ">";
        var start = text.IndexOf(openTag, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return "";

        var closeTag = "</" + tagName + ">";
        var contentStart = start + openTag.Length;
        var end = text.IndexOf(closeTag, contentStart, StringComparison.OrdinalIgnoreCase);
        if (end < 0)
            return "";

        var content = text.Substring(contentStart, end - contentStart);
        return content;
    }

    private record PromptTemplate(string Template, string FormatString, string[] Variables);
}
