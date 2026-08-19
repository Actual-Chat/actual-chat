using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// A single row of a <see cref="TableMarkup"/>: <c>"| cell | cell |"</c>.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public sealed class TableRowMarkup : Markup
{
    [DataMember, Key(0)]
    public TableCellMarkup[] Cells { get; } // Immutable!

    public TableRowMarkup(TableCellMarkup[] cells)
    {
        if (cells.Length == 0)
            throw new ArgumentException("Row should contain at least 1 cell", nameof(cells));

        Cells = cells;
    }

    public TableRowMarkup(IEnumerable<TableCellMarkup> cells)
        : this(cells.ToArray()) { }

    public override string Format()
    {
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        sb.Append(TableMarkup.CellSeparator);
        foreach (var cell in Cells) {
            sb.Append(' ');
            sb.Append(cell.Format());
            sb.Append(' ');
            sb.Append(TableMarkup.CellSeparator);
        }
        return sb.ToStringAndRelease();
    }

    public override Markup Simplify()
    {
        var cells = new TableCellMarkup[Cells.Length];
        var isSimplified = false;
        for (var i = 0; i < Cells.Length; i++) {
            var cell = (TableCellMarkup)Cells[i].Simplify();
            isSimplified |= !ReferenceEquals(cell, Cells[i]);
            cells[i] = cell;
        }
        return isSimplified ? new TableRowMarkup(cells) : this;
    }
}
