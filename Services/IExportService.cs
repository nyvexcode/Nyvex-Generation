using NyvexGeneration.Models;

namespace NyvexGeneration.Services;

public interface IExportService
{
    /// <summary>Um username por linha.</summary>
    Task ExportTxtAsync(IEnumerable<LookupResult> results, string filePath, CancellationToken cancellationToken);

    /// <summary>CSV em UTF-8 com BOM (compatível com Excel).</summary>
    Task ExportCsvAsync(IEnumerable<LookupResult> results, string filePath, CancellationToken cancellationToken, char delimiter = ';');
}
