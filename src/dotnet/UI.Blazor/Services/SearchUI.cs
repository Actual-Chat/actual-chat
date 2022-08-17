namespace ActualChat.UI.Blazor.Services;

public class SearchUI
{
    public IMutableState<string> Text { get; }

    [ComputeMethod]
    public virtual async Task<List<string>> GetKeywords(CancellationToken cancellationToken)
    {
        var text = await Text.Use(cancellationToken).ConfigureAwait(false);
        if (text.IsNullOrEmpty())
            return new List<string>();
        var terms = text.Split().Where(s => !s.IsNullOrEmpty()).ToList();
        return terms;
    }

    public double GetMatchRank(string text, IEnumerable<string> keywords)
    {
        var rank = 0d;
        foreach (var keyword in keywords) {
            var index = -1;
            while (true) {
                index = text.IndexOf(keyword, index + 1, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                    break;
                rank += 1.0 / (1 + index);
            }
        }
        return rank;
    }

    public SearchUI(IStateFactory stateFactory)
        => Text = stateFactory.NewMutable<string>();
}
