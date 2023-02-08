namespace ActualChat.UI.Blazor.Services.Internal;

public abstract record HistoryChangeApplyResult;
public record MustUpdateItemResult : HistoryChangeApplyResult;
public record MustNavigateResult(bool MustReplace) : HistoryChangeApplyResult;
public record MustGoBackResult : HistoryChangeApplyResult;
public record MustFixFirstItemResult(HistoryItem Item1, HistoryItem Item2) : HistoryChangeApplyResult;
