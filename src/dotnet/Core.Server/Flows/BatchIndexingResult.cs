namespace ActualChat.Flows;

public sealed record BatchIndexingResult<TCursor>(bool MustEnd, bool IsTailReached, TCursor? Cursor);
