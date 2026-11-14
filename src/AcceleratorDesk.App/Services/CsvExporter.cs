using System.IO;
using System.Text;

namespace AcceleratorDesk.App.Services;

public static class CsvExporter
{
    public static void Write(string path, IEnumerable<IEnumerable<string>> rows)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        foreach (var row in rows) writer.WriteLine(string.Join(",", row.Select(Escape)));
    }

    private static string Escape(string value)
    {
        var trimmed = value.TrimStart();
        if (trimmed.Length > 0 && ("=+-@".Contains(trimmed[0]) || value[0] is '\t' or '\r' or '\n')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
