namespace CopilotLedger.Cli.Reporting;

internal sealed record TableColumn(string Header, bool RightAligned);

internal static class TableWriter
{
    public static void Write(
        TextWriter output,
        IReadOnlyList<TableColumn> columns,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        int[] widths = columns.Select((column, index) =>
            Math.Max(
                column.Header.Length,
                rows.Count == 0 ? 0 : rows.Max(row => row[index].Length))).ToArray();
        output.WriteLine(FormatLine(
            columns.Select(column => column.Header).ToArray(),
            columns,
            widths));
        foreach (IReadOnlyList<string> row in rows)
        {
            output.WriteLine(FormatLine(row, columns, widths));
        }
    }

    private static string FormatLine(
        IReadOnlyList<string> values,
        IReadOnlyList<TableColumn> columns,
        IReadOnlyList<int> widths)
    {
        string[] cells = new string[values.Count];
        for (int index = 0; index < values.Count; index++)
        {
            cells[index] = columns[index].RightAligned
                ? values[index].PadLeft(widths[index])
                : values[index].PadRight(widths[index]);
        }

        return string.Join("  ", cells).TrimEnd();
    }
}
