namespace ActualChat.Flows;

public sealed record BatchIndexingResult<TCursor>
{
    public required TCursor? Cursor { get; init; }
    public required bool HasProcessedAnyItems { get; init; }
    public required bool IsTailReached { get; init; }
    public string CompletionReason { get; init; } = "";

    public override string ToString()
    {
        var processedSomeItems = HasProcessedAnyItems ? ", processed some items" : "";
        var tailReached = IsTailReached ? ", tail reached" : "";
        var completionReason = CompletionReason.IsNullOrEmpty() ? ""
            : ", CompletionReason=" + JsonFormatter.Format(CompletionReason);
        return $"({Cursor}{processedSomeItems}{tailReached}{completionReason})";
    }
}
