namespace ActualChat.Chat;

/// <summary>
/// Folds Slack legacy cards into Voxt markup and collects their image URLs, in the order the spec documents.
/// </summary>
public static class SlackCardFolder
{
    public static (string Markup, string[] ImageUrls) Fold(InboundPayload payload)
    {
        var blocks = new List<string>();
        var images = new List<string>();
        if (!payload.Text.IsNullOrWhiteSpace())
            blocks.Add(payload.Text.Trim());
        foreach (var card in payload.Attachments) {
            var lines = new List<string>();
            AddLine(lines, card.Pretext);
            if (!card.Title.IsNullOrWhiteSpace()) {
                var title = "**" + card.Title.Trim() + "**";
                if (IsHttpUrl(card.TitleLink))
                    title += " " + card.TitleLink!.Trim();
                lines.Add(title);
            }
            AddLine(lines, card.Text);
            foreach (var field in card.Fields) {
                if (field.Title.IsNullOrWhiteSpace() && field.Value.IsNullOrWhiteSpace())
                    continue;

                lines.Add(field.Title.IsNullOrWhiteSpace()
                    ? field.Value!.Trim()
                    : $"{field.Title.Trim()}: {field.Value?.Trim() ?? ""}");
            }
            AddLine(lines, card.Footer);
            if (lines.Count > 0)
                blocks.Add(string.Join('\n', lines));
            AddImage(images, card.ImageUrl);
            AddImage(images, card.ThumbUrl);
        }
        return (string.Join("\n\n", blocks), images.ToArray());
    }

    private static void AddLine(List<string> lines, string? text)
    {
        if (!text.IsNullOrWhiteSpace())
            lines.Add(text.Trim());
    }

    private static void AddImage(List<string> images, string? url)
    {
        if (images.Count < Constants.WebHooks.InboundImageLimit && IsHttpUrl(url))
            images.Add(url!.Trim());
    }

    private static bool IsHttpUrl(string? url)
        => Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
