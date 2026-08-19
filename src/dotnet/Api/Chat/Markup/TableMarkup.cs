using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

/// <summary>
/// A Markdown (GFM) table: a header row, a per-column alignment, and any number of body rows.
/// Every row has exactly <see cref="ColumnCount"/> cells, and a cell holds inline markup only -
/// a table can't nest any other block markup, nor be nested into one.
/// </summary>
[ParameterComparer(typeof(ByRefParameterComparer))]
[DataContract, MessagePackObject]
public sealed class TableMarkup : BlockMarkup
{
    public const char CellSeparator = '|';

    [DataMember, Key(0)]
    public TableRowMarkup Header { get; }
    [DataMember, Key(1)]
    public TableColumnAlignment[] Alignments { get; } // Immutable!
    [DataMember, Key(2)]
    public TableRowMarkup[] Rows { get; } // Immutable!
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public int ColumnCount => Alignments.Length;

    public TableMarkup(TableRowMarkup header, TableColumnAlignment[] alignments, TableRowMarkup[] rows)
    {
        if (alignments.Length == 0)
            throw new ArgumentException("Table should have at least 1 column", nameof(alignments));
        if (header.Cells.Length != alignments.Length)
            throw new ArgumentException("Header cell count must match the column count", nameof(header));
        foreach (var row in rows)
            if (row.Cells.Length != alignments.Length)
                throw new ArgumentException("Every row must have the same cell count as the header", nameof(rows));

        Header = header;
        Alignments = alignments;
        Rows = rows;
    }

    // '|' is the only character a cell can't contain, so it's the only one that's escapable.
    // MarkupParser.SplitTableRow is the counterpart that turns "\|" back into a literal '|'.
    public static string EscapeCellText(string text)
        => text.Contains(CellSeparator) ? text.Replace("|", @"\|") : text;

    public static string FormatDelimiterCell(TableColumnAlignment alignment)
        => alignment switch {
            TableColumnAlignment.Left => ":--",
            TableColumnAlignment.Center => ":-:",
            TableColumnAlignment.Right => "--:",
            _ => "---",
        };

    public override string Format()
    {
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        sb.Append(Header.Format());
        sb.AppendLine();
        sb.Append(CellSeparator);
        foreach (var alignment in Alignments) {
            sb.Append(' ');
            sb.Append(FormatDelimiterCell(alignment));
            sb.Append(' ');
            sb.Append(CellSeparator);
        }
        foreach (var row in Rows) {
            sb.AppendLine();
            sb.Append(row.Format());
        }
        return sb.ToStringAndRelease();
    }

    public override Markup Simplify()
    {
        var header = (TableRowMarkup)Header.Simplify();
        var isSimplified = !ReferenceEquals(header, Header);
        var rows = new TableRowMarkup[Rows.Length];
        for (var i = 0; i < Rows.Length; i++) {
            var row = (TableRowMarkup)Rows[i].Simplify();
            isSimplified |= !ReferenceEquals(row, Rows[i]);
            rows[i] = row;
        }
        return isSimplified ? new TableMarkup(header, Alignments, rows) : this;
    }
}
