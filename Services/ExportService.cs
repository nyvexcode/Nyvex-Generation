using System.Text;
using NyvexGeneration.Helpers;
using NyvexGeneration.Models;

namespace NyvexGeneration.Services;

public sealed class ExportService : IExportService
{
    public async Task ExportTxtAsync(IEnumerable<LookupResult> results, string filePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(results);
        PrepareDirectory(filePath);

        var builder = new StringBuilder();
        foreach (var result in results)
        {
            builder.Append(result.Username).Append("\r\n");
        }

        await File.WriteAllTextAsync(filePath, builder.ToString(), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
    }

    public async Task ExportCsvAsync(IEnumerable<LookupResult> results, string filePath, CancellationToken cancellationToken, char delimiter = ';')
    {
        ArgumentNullException.ThrowIfNull(results);
        PrepareDirectory(filePath);

        var builder = new StringBuilder();
        builder.Append(string.Join(delimiter, "Username", "Comprimento", "Status", "Detalhes", "DataConsulta")).Append("\r\n");

        foreach (var r in results)
        {
            builder.Append(string.Join(delimiter,
                EscapeCsv(r.Username, delimiter),
                r.Length.ToString(),
                EscapeCsv(StatusFormatter.Text(r.Status), delimiter),
                EscapeCsv(r.Details, delimiter),
                EscapeCsv(r.CheckedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), delimiter)))
                .Append("\r\n");
        }

        await File.WriteAllTextAsync(filePath, builder.ToString(), new UTF8Encoding(true), cancellationToken).ConfigureAwait(false);
    }

    public static string EscapeCsv(string? value, char delimiter)
    {
        var text = value ?? string.Empty;

        // Evita interpretação como fórmula em planilhas.
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@')
        {
            text = "'" + text;
        }

        var mustQuote = text.Contains(delimiter) || text.Contains('"') || text.Contains('\n') || text.Contains('\r');
        return mustQuote ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }

    private static void PrepareDirectory(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Caminho de arquivo inválido.", nameof(filePath));
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
