using System.Text;

namespace Picklebook.Services;

/// <summary>Minimal CSV writer with proper quoting.</summary>
public static class CsvExport
{
    public static string Build(IEnumerable<IEnumerable<string>> rows)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
            sb.AppendLine(string.Join(",", row.Select(Cell)));
        return sb.ToString();
    }

    private static string Cell(string? value)
    {
        if (value is null) return "";
        var v = value.Replace("\"", "\"\"");
        return v.Contains(',') || v.Contains('"') || v.Contains('\n') ? $"\"{v}\"" : v;
    }
}