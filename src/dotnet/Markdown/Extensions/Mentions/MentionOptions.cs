namespace Markdig.Extensions.Mentions;

public class MentionOptions
{
    public Func<string, bool> ValidateMentionId { get; init; } = _ => false;
}
