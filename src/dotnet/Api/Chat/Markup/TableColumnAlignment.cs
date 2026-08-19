namespace ActualChat.Chat;

/// <summary>
/// Alignment of a <see cref="TableMarkup"/> column, defined by its delimiter row cell:
/// <c>---</c>, <c>:--</c>, <c>:-:</c> or <c>--:</c>.
/// </summary>
public enum TableColumnAlignment
{
    None,
    Left,
    Center,
    Right,
}

public static class TableColumnAlignmentExt
{
    public static string? ToTextAlignStyle(this TableColumnAlignment alignment)
        => alignment switch {
            TableColumnAlignment.Left => "text-align: left",
            TableColumnAlignment.Center => "text-align: center",
            TableColumnAlignment.Right => "text-align: right",
            _ => null,
        };
}
