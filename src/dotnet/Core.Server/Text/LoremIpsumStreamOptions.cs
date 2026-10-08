using System.Text.RegularExpressions;

namespace ActualChat.Text;

/// <summary>
/// Arguments of the <c>/lorem-ipsum-stream [seconds][*cps][/ups] [with-reset]</c> admin command.
/// </summary>
public sealed partial record LoremIpsumStreamOptions(
    double Seconds = 5,
    int CharsPerSecond = 100,
    int UpdatesPerSecond = 10,
    bool MustIncludeReset = false)
{
    public const double MinSeconds = 0.5;
    public const double MaxSeconds = 120;
    public const int MaxCharsPerSecond = 2000;
    public const int MaxUpdatesPerSecond = 60;
    public const int MaxTextLength = 50_000;

    [GeneratedRegex(@"^/lorem-ipsum-stream(?:\s+(\d+(?:\.\d+)?)?(?:\*(\d+))?(?:/(\d+))?)?(?:\s+(with-reset))?$")]
    private static partial Regex CommandRegexFactory();

    private static readonly Regex CommandRegex = CommandRegexFactory();

    public int TextLength => (int)Math.Clamp(Math.Round(Seconds * CharsPerSecond), 1, MaxTextLength);

    public static bool TryParse(string text, [NotNullWhen(true)] out LoremIpsumStreamOptions? options)
    {
        var match = CommandRegex.Match(text);
        if (!match.Success) {
            options = null;
            return false;
        }

        var defaults = new LoremIpsumStreamOptions();
        options = new LoremIpsumStreamOptions(
            Math.Clamp(Get(match, 1, defaults.Seconds), MinSeconds, MaxSeconds),
            (int)Math.Clamp(Get(match, 2, defaults.CharsPerSecond), 1, MaxCharsPerSecond),
            (int)Math.Clamp(Get(match, 3, defaults.UpdatesPerSecond), 1, MaxUpdatesPerSecond),
            match.Groups[4].Success);
        return true;
    }

    private static double Get(Match match, int groupIndex, double defaultValue)
        => match.Groups[groupIndex] is { Success: true } group ? double.Parse(group.Value) : defaultValue;
}
