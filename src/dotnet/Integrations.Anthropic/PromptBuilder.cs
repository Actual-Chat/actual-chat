namespace ActualChat.Integrations.Anthropic;

public record PromptTemplate(string Template, string FormatString, string[] Variables);

public interface IPromptUtils
{
    string BuildPrompt(string promptTemplate, IDictionary<string, string> variables);
    string GetXmlTagValue(string text, string tagName);
}

internal sealed class PromptUtils : IPromptUtils
{
    private readonly Dictionary<string, PromptTemplate> _promptTemplates = new (StringComparer.Ordinal);

    public string BuildPrompt(string promptTemplate, IDictionary<string, string> variables)
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

    public string GetXmlTagValue(string text, string tagName)
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
        return new PromptTemplate(
            template,
            formatString,
            variables.Select(v => v.Item1.Substring(2, v.Item1.Length - 4)).ToArray());
    }
}
