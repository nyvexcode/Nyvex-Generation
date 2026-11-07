namespace NyvexGeneration.Models;

public sealed class AppSettings
{
    public string Theme { get; set; } = "DarkNeon";

    public int DefaultQuantity { get; set; } = 100;

    public int DefaultLength { get; set; } = 4;

    public bool AutoCheckAfterGeneration { get; set; } = true;

    public bool AvoidPreviouslyGenerated { get; set; } = true;

    /// <summary>Intervalo conservador entre requisições.</summary>
    public int DelayBetweenRequestsMs { get; set; } = 1500;

    /// <summary>Tamanho de lote por requisição. O limite do endpoint não é documentado; mantido conservador.</summary>
    public int BatchSize { get; set; } = 50;

    public int RequestTimeoutSeconds { get; set; } = 15;

    public int MaxRetriesOnRateLimit { get; set; } = 3;

    public char CsvDelimiter { get; set; } = ';';

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    /// <summary>Ajusta valores fora dos limites suportados.</summary>
    public AppSettings Normalize()
    {
        DefaultQuantity = Math.Clamp(DefaultQuantity, 1, 5000);
        DefaultLength = Math.Clamp(DefaultLength, GenerationOptions.MinSupportedLength, GenerationOptions.MaxSupportedLength);
        DelayBetweenRequestsMs = Math.Clamp(DelayBetweenRequestsMs, 500, 60_000);
        BatchSize = Math.Clamp(BatchSize, 1, 100);
        RequestTimeoutSeconds = Math.Clamp(RequestTimeoutSeconds, 5, 120);
        MaxRetriesOnRateLimit = Math.Clamp(MaxRetriesOnRateLimit, 0, 10);
        if (CsvDelimiter != ',' && CsvDelimiter != ';')
        {
            CsvDelimiter = ';';
        }

        if (string.IsNullOrWhiteSpace(Theme))
        {
            Theme = "DarkNeon";
        }

        return this;
    }
}
